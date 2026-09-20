# Serveur FTPS — Ubuntu 24.04

Dépôt des fichiers du jeu : un canal public, des canaux beta, un compte de publication.
Serveur : `93.127.203.21`.

Rien de ce qui suit n'a été vérifié sur la machine depuis ce poste — les commandes sont à
valider sur place.

## Conventions

| | Compte FTP | Mot de passe | Dossier sur le serveur |
| --- | --- | --- | --- |
| Publication | `swgl-dev` | privé | `/srv/swgl` (lecture/écriture) |
| Canal public | `swgl-public` | `swgl-public` | `/srv/swgl/base` (lecture seule) |
| Beta `<code>` | `swgl-<code>` | `<code>` | `/srv/swgl/beta-<code>` (lecture seule) |

Le préfixe `swgl-` des comptes doit rester identique à `ftp.beta.user.prefix` dans
`launcher.properties` : le testeur ne tape que le code, le launcher reconstitue le compte.

```
/srv/swgl/                    ← racine de swgl-dev
├── base/                     commun à tous les canaux
├── manifest-public.json      manifeste du canal public
├── beta-099cw/               fichiers d'une beta
│   └── manifest.json         son manifeste
└── beta-099cw.diff           son journal

/srv/swgl-jails/              ← dossiers vides servant de racine aux comptes en lecture
├── swgl-public/
└── swgl-099cw/
```

Les comptes en lecture ne sont pas enfermés dans un vrai dossier mais dans une **prison
vide**, où `base`, le dossier de beta et le `.diff` sont montés par `VRootAlias`. C'est ce qui
donne à tous la même vue — `/base/...` et `/beta-099cw/...` — et rend les manifestes
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
     -subj "/CN=93.127.203.21"
sudo chmod 600 /etc/proftpd/ssl/swgl.key
```

Auto-signé : le launcher est réglé pour l'accepter (`ftp.accept.any.certificate=true`).

## 4. Comptes fixes

```bash
sudo useradd --home-dir /srv/swgl --no-create-home \
             --shell /usr/sbin/nologin --gid swgl swgl-dev
sudo passwd swgl-dev

sudo useradd --home-dir /srv/swgl-jails/swgl-public --no-create-home \
             --shell /usr/sbin/nologin --gid swgl swgl-public
sudo passwd swgl-public        # swgl-public
```

`--home-dir` **est** la racine vue par le compte. Le mot de passe de `swgl-public` est
embarqué dans le launcher, il est public de fait ; celui de `swgl-dev` ouvre l'écriture sur
tout le dépôt et ne doit apparaître nulle part côté client.

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

## 8. Créer une beta

```bash
sudo ./add-beta.sh 099cw
```

Le script crée `/srv/swgl/beta-099cw/`, le fichier `beta-099cw.diff`, la prison
`/srv/swgl-jails/swgl-099cw/`, le compte `swgl-099cw` (mot de passe `099cw`), ajoute son bloc
à `swgl-betas.conf` et recharge le service. Le testeur voit alors :

```
/                      prison vide
/base                  lecture seule
/beta-099cw            lecture seule
/beta-099cw.diff
/manifest.json         celui de la beta
```

Fermer une beta : `sudo ./add-beta.sh --remove 099cw`. Le compte et ses accès disparaissent,
les fichiers restent.

## 9. Vérifier

```bash
sudo apt install lftp

lftp -u swgl-public,swgl-public -e "set ssl:verify-certificate no; ls; quit" ftp://93.127.203.21
lftp -u swgl-099cw,099cw        -e "set ssl:verify-certificate no; ls; ls base; quit" ftp://93.127.203.21
lftp -u swgl-dev,MDP            -e "set ssl:verify-certificate no; put /etc/hostname -o t.txt; rm t.txt; quit" ftp://93.127.203.21
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

```properties
ftp.host=93.127.203.21
ftp.port=21
ftp.tls=explicit
ftp.accept.any.certificate=true
ftp.public.user=swgl-public
ftp.public.password=swgl-public
ftp.beta.user.prefix=swgl-
manifest.file=/manifest.json
```

Tous les canaux partageant la même vue, les chemins d'un manifeste sont les mêmes partout :
un fichier commun est en `/base/SWGL/x.pk3`, un fichier de beta en `/beta-099cw/SWGL/x.pk3`.

```bash
# canal public  →  /srv/swgl/manifest-public.json
dotnet run --project Tools/SWGLManifest -- --source <base> --source-prefix /base --channel public

# une beta      →  /srv/swgl/beta-099cw/manifest.json
dotnet run --project Tools/SWGLManifest -- --source <beta-099cw> --source-prefix /beta-099cw \
                                           --base   <base>       --base-prefix   /base \
                                           --channel beta-099cw --version "Ep3 test 4"
```

Le fichier `beta-099cw.diff` n'est pas lu par le launcher : c'est un journal lisible, à
afficher ou distribuer comme tu veux.
