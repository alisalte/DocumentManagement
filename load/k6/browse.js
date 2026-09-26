import http from 'k6/http';
import { check, sleep } from 'k6';

const base = __ENV.BASE_URL || 'http://localhost:5080';
const user = __ENV.USER || 'admin';
const password = __ENV.PASSWORD || 'ChangeMe!Dev12345';

export const options = {
  vus: Number(__ENV.VUS || 20),
  duration: __ENV.DURATION || '2m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:login}': ['p(95)<500'],
    'http_req_duration{name:documents}': ['p(95)<800'],
  },
};

export function setup() {
  const login = http.post(
    `${base}/api/v1/auth/login`,
    JSON.stringify({ username: user, password }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } },
  );
  if (login.status !== 200) {
    throw new Error(`login failed: ${login.status} ${login.body}`);
  }
  return { token: login.json('accessToken') };
}

export default function (data) {
  const headers = { Authorization: `Bearer ${data.token}` };
  const docs = http.get(`${base}/api/v1/documents?page=1&pageSize=25`, {
    headers,
    tags: { name: 'documents' },
  });
  check(docs, { 'documents ok': (r) => r.status === 200 });

  const categories = http.get(`${base}/api/v1/categories`, {
    headers,
    tags: { name: 'categories' },
  });
  check(categories, { 'categories ok': (r) => r.status === 200 });
  sleep(1);
}
