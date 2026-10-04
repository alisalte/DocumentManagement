const tslib = require('tslib/tslib.js');

// pdf-lib imports named helpers from tslib. Metro turns that into
// `const { __extends } = (mod.__esModule ? mod : { default: mod }).default`.
// tslib's CommonJS build sets __esModule and named exports, and has no
// default, so that line throws while the filing screen is loading and the
// phone stays on an empty page. This object satisfies both branches.
const api = {};
for (const key of Object.keys(tslib)) {
  api[key] = tslib[key];
}
api.default = api;
Object.defineProperty(api, '__esModule', { value: true });

module.exports = api;
