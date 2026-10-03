const { withAndroidManifest } = require('@expo/config-plugins');

/** The sideloaded scanner talks to the Docker host over LAN HTTP. */
function withCleartext(config) {
  return withAndroidManifest(config, (config) => {
    const application = config.modResults.manifest.application?.[0];
    if (application) {
      application.$['android:usesCleartextTraffic'] = 'true';
    }
    return config;
  });
}

module.exports = withCleartext;
