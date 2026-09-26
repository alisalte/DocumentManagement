import http from 'k6/http';
import { check, sleep } from 'k6';

const base = __ENV.BASE_URL || 'http://localhost:5080';

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '1m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<50'],
  },
};

export default function () {
  const live = http.get(`${base}/health/live`);
  check(live, { 'live ok': (r) => r.status === 200 });
  const ready = http.get(`${base}/health/ready`);
  check(ready, { 'ready ok': (r) => r.status === 200 });
  sleep(0.2);
}
