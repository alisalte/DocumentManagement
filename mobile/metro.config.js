const { getDefaultConfig } = require('expo/metro-config');
const path = require('path');

const config = getDefaultConfig(__dirname);
const tslibShim = path.resolve(__dirname, 'src/shims/tslib-metro.js');

config.resolver.resolveRequest = (context, moduleName, platform) => {
  if (moduleName === 'tslib') {
    return { type: 'sourceFile', filePath: tslibShim };
  }
  return context.resolveRequest(context, moduleName, platform);
};

module.exports = config;
