#!/usr/bin/env bash
# Grants a user access to a category (and, inherited, everything below it) through the API.
#
# A fresh installation has no ACL entries, and the administrator deliberately gets no document
# content of their own (decision D5), so nobody can file a document until someone is granted
# DOCUMENT_CREATE somewhere. Until the ACL editor arrives (phase 8) this script is how that
# first grant is made. It goes through the API, so every grant is audited like any other.
#
# Usage:
#   scripts/grant-access.sh                       # the signed-in admin gets full access to the root
#   scripts/grant-access.sh --user sara.ahmadi    # another user, full access to the root
#   scripts/grant-access.sh --user sara.ahmadi --category CONTRACTS --read-only
#
# Options:
#   --url URL          API base URL (default: $DMS_URL or http://localhost:8090, the compose stack)
#   --login NAME       who signs in to make the grants (default: admin)
#   --user NAME        who receives them (default: the signed-in user)
#   --category CODE    category code (default: the root category)
#   --read-only        only view, download and print
#
# The password is asked for and never echoed; set DMS_PASSWORD to run without a prompt. An account
# that must change its password (the seeded administrator) is asked for a new one first
# (or DMS_NEW_PASSWORD).
# Entries the user already has on that category, ALLOW or DENY, are reported and left untouched.
set -euo pipefail

URL=${DMS_URL:-http://localhost:8090}
LOGIN=admin
GRANTEE=""
CATEGORY=""
READ_ONLY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --url) URL=$2; shift 2 ;;
        --login) LOGIN=$2; shift 2 ;;
        --user) GRANTEE=$2; shift 2 ;;
        --category) CATEGORY=$2; shift 2 ;;
        --read-only) READ_ONLY=1; shift ;;
        -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
    esac
done

export DMS_URL=$URL DMS_LOGIN=$LOGIN DMS_GRANTEE=$GRANTEE DMS_CATEGORY=$CATEGORY DMS_READ_ONLY=$READ_ONLY

exec python3 - <<'EOF'
import getpass, json, os, sys, urllib.error, urllib.parse, urllib.request

base = os.environ['DMS_URL'].rstrip('/')

READ = ['DOCUMENT_VIEW', 'DOCUMENT_DOWNLOAD', 'DOCUMENT_PRINT']
FULL = READ + [
    'DOCUMENT_VIEW_DRAFT', 'DOCUMENT_CREATE', 'DOCUMENT_EDIT', 'DOCUMENT_CREATE_VERSION',
    'DOCUMENT_DELETE', 'DOCUMENT_RESTORE', 'DOCUMENT_SHARE', 'DOCUMENT_SHARE_EXTERNAL',
]

def call(method, path, body=None, token=None):
    request = urllib.request.Request(
        base + path,
        data=json.dumps(body).encode() if body is not None else None,
        method=method)
    request.add_header('Content-Type', 'application/json')
    if token:
        request.add_header('Authorization', 'Bearer ' + token)
    with urllib.request.urlopen(request) as response:
        text = response.read()
        return json.loads(text) if text else None

def problem(error):
    try:
        body = json.loads(error.read())
        return body.get('detail') or body.get('title') or str(error)
    except Exception:
        return str(error)

password = os.environ.get('DMS_PASSWORD') or getpass.getpass(f"Password for {os.environ['DMS_LOGIN']}: ")
try:
    session = call('POST', '/api/v1/auth/login', {'username': os.environ['DMS_LOGIN'], 'password': password})
except urllib.error.HTTPError as error:
    sys.exit(f'Sign-in failed: {problem(error)}')
except urllib.error.URLError as error:
    sys.exit(f'Cannot reach {base}: {error.reason}')
token = session['accessToken']

# A first sign-in (the seeded administrator, or a reset account) must change the password before
# anything else; the server refuses everything else until then.
if session['user'].get('mustChangePassword'):
    print('This account must change its password first.')
    new_password = os.environ.get('DMS_NEW_PASSWORD') or getpass.getpass('New password: ')
    if not os.environ.get('DMS_NEW_PASSWORD') and getpass.getpass('Repeat it: ') != new_password:
        sys.exit('The passwords differ.')
    try:
        call('POST', '/api/v1/auth/change-password', {'currentPassword': password, 'newPassword': new_password}, token=token)
        session = call('POST', '/api/v1/auth/login', {'username': os.environ['DMS_LOGIN'], 'password': new_password})
    except urllib.error.HTTPError as error:
        sys.exit(f'Changing the password failed: {problem(error)}')
    token = session['accessToken']
    print('Password changed.')

grantee_name = os.environ['DMS_GRANTEE']
if grantee_name:
    found = call('GET', '/api/v1/directory/users?search=' + urllib.parse.quote(grantee_name), token=token)
    matches = [user for user in found if user['username'].lower() == grantee_name.lower()]
    if not matches:
        sys.exit(f"No active user '{grantee_name}'.")
    grantee = matches[0]['id']
else:
    grantee_name, grantee = session['user']['username'], session['user']['id']

categories = call('GET', '/api/v1/categories', token=token)
code = os.environ['DMS_CATEGORY']
category = next((c for c in categories if (c['code'].lower() == code.lower() if code else c['parentId'] is None)), None)
if category is None:
    sys.exit(f"No category '{code}' (codes are case-insensitive)." if code else 'No root category found.')

# Granting an existing (resource, subject, permission) replaces it on the server, which would turn
# a deliberate DENY into an ALLOW. Existing entries are therefore left alone.
acl_path = f"/api/v1/resources/Category/{category['id']}/permissions"
existing = {
    entry['permissionCode']: entry
    for entry in call('GET', acl_path, token=token)
    if entry['subjectType'] == 'User' and entry['subjectId'] == grantee
}

permissions = READ if os.environ['DMS_READ_ONLY'] == '1' else FULL
print(f"Granting {grantee_name} on '{category['name']}' ({category['code']}), inherited by everything below it:")
failed = False
for permission in permissions:
    if permission in existing:
        entry = existing[permission]
        note = ' (left as it is; revoke it first to change it)' if entry['effect'] == 'Deny' else ''
        print(f"  exists   {permission}: {entry['effect']}{note}")
        continue
    try:
        call('POST', f"/api/v1/resources/Category/{category['id']}/permissions", {
            'subjectType': 'User',
            'subjectId': grantee,
            'permissionCode': permission,
            'effect': 'Allow',
            'inherit': True,
            'reason': 'grant-access.sh',
        }, token)
        print(f'  granted  {permission}')
    except urllib.error.HTTPError as error:
        failed = True
        print(f'  FAILED   {permission}: {problem(error)}')

sys.exit(1 if failed else 0)
EOF
