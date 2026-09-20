#!/usr/bin/env bash
# ==============================================================================
# Cree une beta : dossier, fichier .diff, prison, compte FTP et bloc ProFTPD.
#
#   sudo ./add-beta.sh 099cw
#
# Donne le compte "swgl-099cw", mot de passe "099cw", qui voit :
#   /                    prison vide
#   /base                le contenu commun, en lecture seule
#   /beta-099cw          les fichiers de la beta
#   /beta-099cw.diff     son journal
#   /manifest.json       le manifeste de la beta
#
# Suppression :  sudo ./add-beta.sh --remove 099cw
# ==============================================================================
set -euo pipefail

ROOT=/srv/swgl
JAILS=/srv/swgl-jails
GROUP=swgl
OWNER=swgl-dev
BETAS_CONF=/etc/proftpd/conf.d/swgl-betas.conf

# Prefixe des comptes : doit correspondre a ftp.beta.user.prefix du launcher.
USER_PREFIX=swgl-
# Prefixe des dossiers et des .diff sur le disque.
DIR_PREFIX=beta-

usage() {
    echo "usage: $0 [--remove] <code>" >&2
    echo "   ex: $0 099cw" >&2
    exit 1
}

[[ $# -ge 1 ]] || usage
[[ $EUID -eq 0 ]] || { echo "a lancer en root (sudo)" >&2; exit 1; }

remove=false
if [[ "$1" == "--remove" ]]; then
    remove=true
    shift
    [[ $# -eq 1 ]] || usage
fi

code="${1:-}"
[[ -n "$code" ]] || usage
[[ "$code" =~ ^[A-Za-z0-9_-]+$ ]] || { echo "code invalide : $code" >&2; exit 1; }

user="${USER_PREFIX}${code}"
jail="${JAILS}/${user}"
home="${ROOT}/${DIR_PREFIX}${code}"
diff="${ROOT}/${DIR_PREFIX}${code}.diff"

# ------------------------------------------------------------------ suppression
if $remove; then
    echo "Suppression de la beta ${code}"
    userdel "$user" 2>/dev/null || echo "  (compte ${user} deja absent)"
    rmdir "$jail" 2>/dev/null || true

    # Retire le bloc <IfUser ...> correspondant.
    if [[ -f "$BETAS_CONF" ]]; then
        awk -v u="$user" '
            $0 ~ "^<IfUser " u ">$" { skip = 1 }
            skip == 0 { print }
            $0 == "</IfUser>" && skip == 1 { skip = 0 }
        ' "$BETAS_CONF" > "${BETAS_CONF}.tmp"
        mv "${BETAS_CONF}.tmp" "$BETAS_CONF"
    fi

    proftpd --configtest && systemctl reload proftpd
    echo "  compte et acces supprimes."
    echo "  les fichiers restent en place : ${home} et ${diff}"
    exit 0
fi

# ------------------------------------------------------------------- creation
if id "$user" &>/dev/null; then
    echo "le compte ${user} existe deja" >&2
    exit 1
fi

echo "Creation de la beta ${code}"

# Les fichiers de la beta appartiennent au compte de publication.
install -d -o "$OWNER" -g "$GROUP" -m 755 "$home"
[[ -f "$diff" ]] || install -o "$OWNER" -g "$GROUP" -m 644 /dev/null "$diff"

# La prison est vide et appartient a root : rien ne peut y etre ecrit.
install -d -o root -g root -m 755 "$jail"

useradd --home-dir "$jail" --no-create-home --shell /usr/sbin/nologin \
        --gid "$GROUP" "$user"

# Mot de passe = code : qui connait le code a de toute facon acces a la beta.
echo "${user}:${code}" | chpasswd

touch "$BETAS_CONF"
cat >> "$BETAS_CONF" <<EOF

<IfUser ${user}>
  DefaultRoot ${jail}
  VRootAlias ${ROOT}/base base
  VRootAlias ${home} ${DIR_PREFIX}${code}
  VRootAlias ${diff} ${DIR_PREFIX}${code}.diff
  VRootAlias ${home}/manifest.json manifest.json
</IfUser>
EOF

proftpd --configtest && systemctl reload proftpd

echo
echo "  compte       : ${user}"
echo "  mot de passe : ${code}"
echo "  il voit      : /base  /${DIR_PREFIX}${code}  /${DIR_PREFIX}${code}.diff  /manifest.json"
echo
echo "  a donner aux testeurs : ${code}"
