const { I18nManager } = require('react-native');

I18nManager.allowRTL(true);
I18nManager.forceRTL(true);

if (typeof document !== 'undefined') {
  document.documentElement.setAttribute('dir', 'rtl');
}

require('expo-router/entry');
