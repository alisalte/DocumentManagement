import http from 'k6/http';
import { check, sleep } from 'k6';

const base = __ENV.BASE_URL || 'http://localhost:5080';
const user = __ENV.USER || 'admin';
const password = __ENV.PASSWORD || 'ChangeMe!Dev12345';
const query = __ENV.QUERY || 'سند';

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '2m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:search}': ['p(95)<1000'],
  },
};

export function setup() {
  const login = http.post(
    `${base}/api/v1/auth/login`,
    JSON.stringify({ username: user, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );
  if (login.status !== 200) {
    throw new Error(`login failed: ${login.status} ${login.body}`);
  }
  return { token: login.json('accessToken') };
}

export default function (data) {
  const response = http.get(`${base}/api/v1/search?q=${encodeURIComponent(query)}`, {
    headers: { Authorization: `Bearer ${data.token}` },
    tags: { name: 'search' },
  });
  check(response, { 'search ok': (r) => r.status === 200 });
  sleep(0.5);
}
