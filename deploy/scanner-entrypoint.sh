#!/bin/sh
# Starts nginx for the scanner image. Creates a self-signed cert on first boot so phones
# can use getUserMedia over HTTPS without a public CA.
set -eu

CERT_DIR=/etc/nginx/certs
mkdir -p "$CERT_DIR"

HOST="${SCANNER_TLS_HOST:-localhost}"
IP="${SCANNER_TLS_IP:-}"

if [ ! -f "$CERT_DIR/fullchain.pem" ] || [ ! -f "$CERT_DIR/privkey.pem" ]; then
  echo "scanner: generating self-signed TLS certificate for ${HOST}${IP:+ and ${IP}}"
  SAN="DNS:localhost,DNS:${HOST},IP:127.0.0.1"
  # Accept a comma-separated list of extra IPs, e.g. 62.60.166.71,192.168.1.10
  if [ -n "$IP" ]; then
    OLD_IFS=$IFS
    IFS=,
    for item in $IP; do
      item=$(echo "$item" | tr -d ' ')
      [ -n "$item" ] && SAN="${SAN},IP:${item}"
    done
    IFS=$OLD_IFS
  fi
  openssl req -x509 -nodes -newkey rsa:2048 -days 825 \
    -keyout "$CERT_DIR/privkey.pem" \
    -out "$CERT_DIR/fullchain.pem" \
    -subj "/CN=${HOST}" \
    -addext "subjectAltName=${SAN}"
fi

# Replace the base image's log symlinks. nginx opens them with O_CREAT before it
# reads the config; a non-root master gets EACCES on /dev/stderr and exits.
LOG_DIR=/var/log/nginx
mkdir -p "$LOG_DIR" /tmp/nginx /var/cache/nginx \
  /var/cache/nginx/client_temp /var/cache/nginx/proxy_temp \
  /var/cache/nginx/fastcgi_temp /var/cache/nginx/uwsgi_temp /var/cache/nginx/scgi_temp
for log in error.log access.log; do
  target="$LOG_DIR/$log"
  if [ -L "$target" ] || [ ! -e "$target" ]; then
    rm -f "$target"
    touch "$target"
  fi
done

chown -R nginx:nginx "$CERT_DIR" "$LOG_DIR" /var/cache/nginx /tmp/nginx /usr/share/nginx/html
exec su-exec nginx nginx -g 'daemon off;'
