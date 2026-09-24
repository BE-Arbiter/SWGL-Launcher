# Serveur FTPS — Ubuntu 24.04

Dépôt des fichiers du jeu : un canal public, des canaux beta, un compte de publication.
Serveur : `vps-c2b14a7e.vps.ovh.net`.

Rien de ce qui suit n'a été vérifié sur la machine depuis ce poste — les commandes sont à
valider sur place.

## Conventions

| | Compte FTP | Mot de passe | Dossier sur le serveur |
| --- | --- | --- | --- |
| Publication | `swgl-dev` | privé | `/srv/swgl` (lecture/écriture) |
| Canal public | `swgl-public` | `swgl-public` | `/srv/swgl/base` (lecture seule) |
| Beta `<code>` | `swgl-<code>` | `<code>` | `/srv/swgl/beta-<code>` (lecture seule) |

Le préfixe `swgl-` des comptes doit rester identique à `ftp.beta.user.prefix`
(défaut du launcher) : le testeur ne tape que le code, le launcher reconstitue le compte.

```
/srv/swgl/                    ← racine de swgl-dev
├── base/                     commun à tous les canaux
├── manifest-public.json      manifeste du canal public
├── patchnotes-public.md      notes de version du public
├── beta-elween/              fichiers d'une beta
│   └── manifest.json         son manifeste
├── beta-elween.diff          son journal
└── patchnotes-elween.md      ses notes de version

/srv/swgl-jails/              ← dossiers vides servant de racine aux comptes en lecture
├── swgl-public/
└── swgl-elween/
```

Les comptes en lecture ne sont pas enfermés dans un vrai dossier mais dans une **prison
vide**, où `base`, le dossier de beta et le `.diff` sont montés par `VRootAlias`. C'est ce qui
donne à tous la même vue — `/base/...` et `/beta-elween/...` — et rend les manifestes
interchangeables entre canaux.

## Pourquoi ProFTPD

Chaque beta doit voir **son** dossier *et* le dossier commun. vsftpd ne sait pas faire
apparaître un chemin extérieur dans une prison : il faudrait un *bind mount* par beta, donc
une ligne d'`/etc/fstab` à chaque création. ProFTPD le fait en configuration pure avec
`mod_vroot`.

## 1. Installation

```bash
sudo apt update
sudo apt install proftpd-core proftpd-mod-crypto
ls /usr/lib/proftpd/ | grep -E 'mod_(tls|vroot)'
```

`mod_tls` est dans `proftpd-mod-crypto`, **pas** dans `proftpd-core` — sans lui ProFTPD
refuse de démarrer sur un `LoadModule mod_tls.c`. `mod_ifsession`, lui, est compilé en
statique : ne pas le charger, ProFTPD s'en plaindrait. `proftpd -l` liste ce qui est déjà
intégré.

Deux directives à ne pas utiliser sur ce paquet : `IdentLookups` (module `mod_ident` absent)
et tout `LoadModule` pour un module statique.

## 2. Arborescence

```bash
sudo groupadd -f swgl

sudo install -d -o root -g swgl -m 755 /srv/swgl
sudo install -d -o root -g swgl -m 755 /srv/swgl/base

sudo install -d -o root -g root -m 755 /srv/swgl-jails
sudo install -d -o root -g root -m 755 /srv/swgl-jails/swgl-public
```

Les prisons sont **hors** de `/srv/swgl`, sinon `swgl-dev` les verrait traîner dans son
arborescence.

## 3. Certificat TLS

```bash
sudo install -d -m 700 /etc/proftpd/ssl
sudo openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
     -keyout /etc/proftpd/ssl/swgl.key \
     -out    /etc/proftpd/ssl/swgl.crt \
     -subj "/CN=vps-c2b14a7e.vps.ovh.net"
sudo chmod 600 /etc/proftpd/ssl/swgl.key
```

Auto-signé : le launcher est réglé pour l'accepter (`ftp.accept.any.certificate=true`).

## 4. Comptes fixes

```bash
sudo useradd --home-dir /srv/swgl --no-create-home \
             --shell /bin/sh --gid swgl swgl-dev
sudo passwd swgl-dev

sudo useradd --home-dir /srv/swgl-jails/swgl-public --no-create-home \
             --shell /usr/sbin/nologin --gid swgl swgl-public
sudo passwd swgl-public        # swgl-public
```

`--home-dir` **est** la racine vue par le compte. Le mot de passe de `swgl-public` est
embarqué dans le launcher, il est public de fait ; celui de `swgl-dev` ouvre l'écriture sur
tout le dépôt et ne doit apparaître nulle part côté client.

`swgl-dev` a `/bin/sh` et non `nologin` pour pouvoir lancer `swgl-sync` en SSH (section 8) ;
sshd ne lui laisse de toute façon aucun shell. Pas `/bin/bash` : bash lit le `.bashrc` du
dossier personnel quand sshd le lance, et ce dossier, `/srv/swgl`, est modifiable par FTP.

## 5. Droits

```bash
sudo chown -R swgl-dev:swgl /srv/swgl
sudo find /srv/swgl -type d -exec chmod 755 {} \;
sudo find /srv/swgl -type f -exec chmod 644 {} \;
```

ProFTPD interdit l'écriture au niveau du protocole, ces permissions l'interdisent au niveau
du système. Les deux, pas l'une ou l'autre.

## 6. Configuration

```bash
sudo cp proftpd-swgl.conf /etc/proftpd/conf.d/swgl.conf
sudo touch /etc/proftpd/conf.d/swgl-betas.conf
sudo proftpd --configtest
sudo systemctl restart proftpd
```

Toujours `--configtest` avant de recharger : une faute de syntaxe empêche le service de
redémarrer, et il ne remonte pas tout seul.

## 7. Pare-feu

```bash
sudo ufw allow 21/tcp
sudo ufw allow 49152:49200/tcp
```

Derrière un NAT, décommenter `MasqueradeAddress` dans la configuration, sans quoi le mode
passif annoncerait une adresse privée injoignable.

## 8. L'outil `swgl-sync`

Il gère les branches du dépôt : le public et les betas. L'installer depuis GitHub, et non en
copiant un fichier Windows — un fichier en fins de ligne CRLF ne s'exécute pas sous bash :

```bash
sudo curl -fsSLo /usr/local/sbin/swgl-sync \
     https://raw.githubusercontent.com/BE-Arbiter/SWGL-Launcher/main/server/swgl-sync
sudo chmod +x /usr/local/sbin/swgl-sync
```

Il s'appuie sur `SWGLManifest`, à installer dans `/usr/local/bin` (voir le README principal
pour le compiler en `linux-x64`).

| Commande | Effet |
| --- | --- |
| `swgl-sync update [--notify ["<message>"]]` | Régénère le manifeste du public, puis celui de chaque beta. |
| `swgl-sync update --branch <branche> [--label "<libellé>"] [--notify ["<message>"]]` | Régénère une seule branche : `public` ou un code de beta. |
| `swgl-sync rename --branch <branche> --label "<libellé>"` | Change le libellé affiché aux joueurs, sans rien recalculer. |
| `swgl-sync create --branch <code>` | Crée une beta. |
| `swgl-sync remove --branch <code>` | Ferme une beta ; ses fichiers restent sur le disque. |
| `swgl-sync exclude --branch <code> [chemin...]` | Retire des fichiers de `base` pour cette beta ; sans chemin, affiche la liste. |
| `swgl-sync restore --branch <code> <chemin...>` | Annule un retrait. |
| `swgl-sync force-delete --branch <branche> [chemin...]` | Fait supprimer des fichiers chez les joueurs, hors des motifs du launcher ; sans chemin, affiche la liste. |
| `swgl-sync cancel-delete --branch <branche> <chemin...>` | Annule une suppression forcée. |

Le libellé est la version affichée aux joueurs. Sans libellé, `update` garde celui déjà publié ;
une branche qui n'en a encore aucun reçoit la date du jour (`2026.09.22.2143`). Une valeur qui
contient des espaces se met entre guillemets : `swgl-sync rename --branch elween --label "Ep3 test 4"`.
Les options peuvent venir dans n'importe quel ordre ; les chemins se placent après.

### Créer une beta

```bash
sudo swgl-sync create --branch elween
```

Crée `/srv/swgl/beta-elween/`, le fichier `beta-elween.diff`, la prison
`/srv/swgl-jails/swgl-elween/`, le compte `swgl-elween` (mot de passe `elween`), ajoute son
bloc à `swgl-betas.conf` et recharge ProFTPD après un `--configtest`. Il reste à déposer dans
`beta-elween/` les fichiers qui diffèrent de `base`, en respectant l'arborescence du GameData,
puis :

```bash
sudo swgl-sync update --branch elween --label "Ep3 test 1"
```

Le testeur voit alors :

```
/                      prison vide
/base                  lecture seule
/beta-elween           lecture seule
/beta-elween.diff
/manifest.json         celui de la beta
/patchnotes.md         ses notes de version
```

### Retirer des fichiers de `base` pour une beta

Une beta peut ajouter ou remplacer des fichiers en les déposant dans son dossier ; pour en
**retirer** un que `base` fournit, on le marque :

```bash
sudo swgl-sync exclude --branch elween SWGL/SWGL_Missions_Ep8.pk3 "SWGL/SWGL_Old_*.pk3"
sudo swgl-sync exclude --branch elween          # affiche la liste
sudo swgl-sync restore --branch elween SWGL/SWGL_Missions_Ep8.pk3
sudo swgl-sync update  --branch elween          # applique
```

Les chemins sont relatifs au GameData. `*` reste dans un dossier, `**` le traverse ; dans un
shell, un motif se met entre guillemets pour que le shell ne l'interprète pas. À l'invite
`swgl-sync>`, les guillemets sont acceptés mais inutiles, et ils ne finissent jamais dans la
liste. La liste est stockée dans
`/srv/swgl/beta-elween.remove`, hors du dossier de la beta : ni publiée, ni visible des
testeurs.

Le retrait ne porte que sur `base` : un fichier déposé dans le dossier de la beta elle-même
reste publié. Un chemin qui ne correspond à rien est signalé, au marquage comme à la mise à
jour — c'est presque toujours une faute de frappe.

Côté launcher, rien de spécial : le fichier ne figure plus au manifeste de la beta, il est
donc supprimé chez le testeur, puis retéléchargé s'il repasse en public.

Cette fonction demande un `SWGLManifest` récent (option `--remove-list`) : l'ancien répond
`Argument inconnu`.

### Forcer la suppression de fichiers chez les joueurs

Quand un canal ne publie plus un fichier, le launcher le supprime s'il l'avait lui-même installé
(les fichiers d'une beta disparaissent au retour sur le public), ou s'il correspond à
`base/zzzzzzz_SWGL_JKJO.pk3`, `SWGL/SWGL_*.pk3` ou `SWGL/*.dll`. Pour faire disparaître autre
chose, par exemple une map ou une config qu'une ancienne version installée à la main a laissée,
on le marque sur la branche concernée, `public` ou un code de beta :

```bash
sudo swgl-sync force-delete --branch public "SWGL/maps/old_*.bsp" SWGL/readme.txt
sudo swgl-sync force-delete --branch public     # affiche la liste
sudo swgl-sync cancel-delete --branch public SWGL/readme.txt
sudo swgl-sync update --branch public           # applique
```

La liste est stockée dans `/srv/swgl/forcedelete-<branche>.list`. `update` l'inscrit dans le
manifeste (clé `delete`), et le launcher supprime alors ces fichiers chez le joueur. Il le fait
à chaque mise à jour : un fichier supprimé qui réapparaît est de nouveau supprimé, tant que le
motif reste dans la liste. Les suppressions forcées du public valent aussi pour les betas.

Un motif part de la racine du GameData : `*.cfg` ne couvre que les `.cfg` à la racine, et
`SWGL/maps/*` ne descend pas dans les sous-dossiers de `maps` (il faut `SWGL/maps/**`).

Deux garde-fous, côté launcher : un fichier que le canal publie n'est jamais supprimé (l'outil
de manifeste le signale), et le launcher, sa configuration et son état non plus. En dehors de
ça, un motif trop large supprime tout ce qu'il couvre : `base/**` viderait le `base` de Jedi
Academy chez tous les joueurs. Il faut préférer des chemins exacts.

Cette fonction demande un `SWGLManifest` et un launcher récents : l'ancien outil répond
`Argument inconnu : --delete-list`, et un ancien launcher ignore la clé `delete`.

### Mettre à jour `base`

Une beta reprend les fichiers de `base` depuis le manifeste public, avec leurs empreintes déjà
calculées : seuls ses propres fichiers sont relus, ce qui la régénère en quelques secondes. Les
suppressions forcées du public valent aussi pour elle.

Le public lui-même ne relit que ce qui a changé : chaque manifeste mémorise la taille et la
date de chaque fichier, et une empreinte n'est recalculée que pour un fichier nouveau, ou dont
la taille ou la date diffère. La première génération avec cet outil relit tout une dernière
fois. Pour forcer une relecture complète : `SWGLManifest ... --rehash`.

Après une modification de `base`, il faut donc régénérer le public **puis** les betas —
`swgl-sync update` sans argument le fait dans cet ordre — sans quoi les testeurs se heurteraient
à des empreintes périmées. L'outil le rappelle quand on ne met à jour que le public.

### Notes de version

Chaque branche a son fichier Markdown à la racine du dépôt : `patchnotes-public.md`,
`patchnotes-elween.md`… Chaque compte le voit en `/patchnotes.md`. Le launcher le lit avec le
manifeste et, s'il n'est pas vide, affiche un lien *Patch notes* à droite de la version.

Pour publier ou corriger des notes, il suffit de déposer le fichier par FTP avec `swgl-dev` :
pas besoin de `swgl-sync update`, le launcher le relit à chaque vérification. Pour retirer les
notes, on vide le fichier. Il ne faut pas le supprimer : l'alias FTP pointe dessus.

`swgl-sync` crée ces fichiers vides : `create` pour une nouvelle beta, `update` pour le public
et pour chaque beta. Une beta créée avant l'ajout des notes reçoit son alias au prochain
`swgl-sync update --branch <code>`.

Le HTML brut est ignoré à l'affichage, et les liens s'ouvrent dans le navigateur du joueur.

Sur un serveur installé avant l'ajout des notes, le public a besoin de son alias, à ajouter
une seule fois dans le bloc `<IfUser swgl-public>` de `/etc/proftpd/conf.d/swgl.conf` :

```
    VRootAlias        /srv/swgl/patchnotes-public.md patchnotes.md
```

puis :

```bash
sudo install -o swgl-dev -g swgl -m 644 /dev/null /srv/swgl/patchnotes-public.md
sudo proftpd --configtest && sudo systemctl reload proftpd
```

### Annonces Discord

`update --notify` publie, une fois toutes les branches traitées, **un** message Discord qui
liste celles dont le manifeste a changé :

```bash
sudo swgl-sync update --notify "@Testers New map: Kashyyyk"
sudo swgl-sync update --branch elween --label "Ep3 test 5" --notify
```

```
@Testers New map: Kashyyyk

New branch: elween — Ep3 test 5
• 214 files added (15.2 GB to download)

Branch updated: public — Release 3
• 3 files added, 1 updated, 1 removed (812 MB to download)

📎 changes.txt
```

- Une branche est « changée » si ses fichiers (ajoutés, modifiés, retirés) ou sa liste de
  suppressions forcées ont bougé ; un simple changement de libellé ne compte pas. Une branche
  sans manifeste précédent apparaît en *New branch*.
- Quand `base` change, les betas changent aussi, puisqu'elles l'embarquent : elles apparaissent.
- Sans changement, rien n'est envoyé. Sans message, seul le résumé part.
- Le détail fichier par fichier est dans `changes.txt`, joint au message : Discord en montre un
  aperçu dépliable.
- `@Nom` devient une vraie mention seulement si le rôle est déclaré dans la configuration ;
  aucune autre mention n'est jamais envoyée (pas de `@everyone` par accident).

La configuration tient dans `/etc/swgl-sync.conf`, lisible par root seul — l'URL du webhook
permet à quiconque la connaît de publier dans le salon :

```bash
sudo tee /etc/swgl-sync.conf >/dev/null <<'EOF'
webhook=https://discord.com/api/webhooks/<id>/<jeton>
role.Testers=123456789012345678
EOF
sudo chmod 600 /etc/swgl-sync.conf
```

Le webhook se crée dans Discord : *Paramètres du salon → Intégrations → Webhooks*. L'identifiant
d'un rôle se copie par clic droit sur le rôle, une fois le *mode développeur* activé
(*Paramètres → Avancés*). Si la mention ne sonne pas, cocher *Autoriser tout le monde à
@mentionner ce rôle* dans les réglages du rôle.

`--notify` vérifie la présence du webhook avant de commencer : sans lui, rien n'est régénéré.
Si Discord refuse le message, les manifestes restent à jour ; seule l'annonce manque.

### `swgl-sync` pour `swgl-dev`, en SSH

`swgl-dev` peut lancer `swgl-sync` en SSH, et rien d'autre : ni shell, ni SFTP/SCP, ni
tunnel. Il s'identifie avec le même mot de passe que pour le FTP.

```bash
BASE=https://raw.githubusercontent.com/BE-Arbiter/SWGL-Launcher/main/server
sudo curl -fsSLo /usr/local/sbin/swgl-sync-ssh "$BASE/swgl-sync-ssh"
sudo chmod 755 /usr/local/sbin/swgl-sync-ssh

sudo curl -fsSLo /etc/sudoers.d/swgl-dev "$BASE/sudoers-swgl-dev"
sudo chmod 440 /etc/sudoers.d/swgl-dev
sudo visudo -c

sudo curl -fsSLo /etc/ssh/sshd_config.d/swgl-dev.conf "$BASE/sshd-swgl-dev.conf"
sudo sshd -t && sudo systemctl restart ssh
```

Une session SSH ouverte sans commande donne l'invite `swgl-sync>` ; avec une commande, elle
l'exécute et se ferme :

```bash
ssh swgl-dev@vps-c2b14a7e.vps.ovh.net update --branch elween
```

À l'invite, les flèches ↑/↓ parcourent l'historique de la session, ←/→ et Début/Fin
éditent la ligne, Ctrl+R cherche dans l'historique, et Tab complète les commandes, leurs
options (`--branch`, `--label`, `--notify`), les branches et les chemins (fichiers de `base` et de la beta, entrées des listes pour `restore`
et `cancel-delete`). Le `swgl-sync` devant la commande est facultatif. Une ligne n'est jamais
interprétée par un shell, seulement découpée en mots, les guillemets groupant des mots
(`"Ep3 test 4"`) : `$(...)`, `;` ou `*` restent du texte. Ctrl+C interrompt la commande en
cours ou efface la ligne, `exit` ou Ctrl+D ferment la session.

Le script d'entrée est en Python 3, présent d'office sur Ubuntu (`python3 --version`). Pas en
bash : l'édition de ligne de bash (`read -e`) garde des raccourcis qui exécutent des commandes
(Ctrl+X Ctrl+E ouvre un éditeur puis exécute son contenu), une porte de sortie vers un shell.
Celle de Python ne fait qu'éditer du texte, et le script ignore tout `.inputrc`.

Dans MobaXterm, *Advanced SSH settings → Execute command* permet d'enregistrer une session
par commande courante.

Ce qui tient le verrou :

- `ForceCommand` : quoi que demande le client, sshd lance `swgl-sync-ssh` ;
- sudoers : `swgl-dev` n'obtient root que pour `/usr/local/sbin/swgl-sync`, et sudo remet
  l'environnement à zéro (pas de `MANIFEST_TOOL` ni de `PATH` injectés) ;
- `swgl-sync` et `swgl-sync-ssh` appartiennent à root : `swgl-dev` ne peut pas les modifier ;
- `/bin/sh` comme shell et `AuthorizedKeysFile none` : rien de ce que `swgl-dev` dépose par
  FTP dans `/srv/swgl` n'est lu par sshd.

Pour passer aux clés plus tard : déposer la clé publique dans
`/etc/ssh/authorized_keys/swgl-dev` (root, 644), remplacer `none` par
`/etc/ssh/authorized_keys/%u` et ajouter `AuthenticationMethods publickey` dans le bloc.

Vérifier que la config est appliquée :

```bash
sudo sshd -T -C user=swgl-dev,host=test,addr=127.0.0.1 | grep -Ei "forcecommand|permittty|disableforwarding"
```

## 9. Vérifier

```bash
sudo apt install lftp

lftp -u swgl-public,swgl-public -e "set ssl:verify-certificate no; ls; quit" ftp://vps-c2b14a7e.vps.ovh.net
lftp -u swgl-elween,elween      -e "set ssl:verify-certificate no; ls; ls base; quit" ftp://vps-c2b14a7e.vps.ovh.net
lftp -u swgl-dev,MDP            -e "set ssl:verify-certificate no; put /etc/hostname -o t.txt; rm t.txt; quit" ftp://vps-c2b14a7e.vps.ovh.net
```

Les trois contrôles qui comptent : le public ne voit **pas** les dossiers de beta, une beta ne
peut **pas** écrire (le `put` doit échouer), et une connexion sans TLS est refusée
(`set ftp:ssl-allow false` doit faire échouer la commande).

## 10. Auto-ban

Le code d'une beta est son seul secret : il faut rendre le bruteforce inutile.

```bash
sudo apt install fail2ban
sudo tee /etc/fail2ban/jail.d/proftpd.conf >/dev/null <<'EOF'
[proftpd]
enabled  = true
port     = ftp,ftp-data,49152:49200
logpath  = /var/log/proftpd/proftpd.log
maxretry = 10
findtime = 1h
bantime  = 1h
EOF
sudo systemctl restart fail2ban
sudo fail2ban-client status proftpd
```

## 11. Côté launcher

Ce sont les valeurs par défaut du launcher : aucun `launcher.properties` n'est nécessaire.
À n'écrire que pour s'en écarter.

```properties
ftp.host=vps-c2b14a7e.vps.ovh.net
ftp.port=21
ftp.tls=explicit
ftp.accept.any.certificate=true
ftp.public.user=swgl-public
ftp.public.password=swgl-public
ftp.beta.user.prefix=swgl-
manifest.file=/manifest.json
patchnotes.file=/patchnotes.md
```

Tous les canaux partageant la même vue, les chemins d'un manifeste sont les mêmes partout :
un fichier commun est en `/base/SWGL/x.pk3`, un fichier de beta en `/beta-elween/SWGL/x.pk3`.
`swgl-sync update` génère les manifestes en conséquence.

Côté testeur : *Options* → *Beta channel* → *Add beta code...* → `elween`.

Le fichier `beta-elween.diff` n'est pas lu par le launcher : c'est un journal lisible, à
afficher ou distribuer comme tu veux.
