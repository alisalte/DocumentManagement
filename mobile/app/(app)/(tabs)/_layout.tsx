import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Tabs } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, shadow } from '../../../src/theme';

/** The tab bar matches the web app's phone bar: outline icons, with scanning as the raised brand action. */
export default function TabsLayout() {
  const insets = useSafeAreaInsets();
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.accent,
        tabBarInactiveTintColor: colors.muted,
        tabBarStyle: {
          backgroundColor: colors.card,
          borderTopColor: colors.line,
          height: 72 + insets.bottom,
          paddingBottom: 8 + insets.bottom,
          paddingTop: 8,
        },
        tabBarLabelStyle: {
          fontFamily: 'Vazir-Medium',
          fontSize: 11,
          // Vazirmatn's dots sit low; a tight line height clips them.
          lineHeight: 24,
          height: 24,
          writingDirection: 'rtl',
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'بایگانی',
          tabBarIcon: ({ color, focused }) => (
            <MaterialCommunityIcons name={focused ? 'folder' : 'folder-outline'} color={color} size={24} />
          ),
        }}
      />
      <Tabs.Screen
        name="capture"
        options={{
          title: 'اسکن',
          tabBarIcon: ({ focused }) => (
            <View style={[styles.scan, focused && styles.scanFocused]}>
              <MaterialCommunityIcons name="line-scan" color="#ffffff" size={24} />
            </View>
          ),
        }}
      />
      <Tabs.Screen
        name="search"
        options={{
          title: 'جستجو',
          tabBarIcon: ({ color }) => <MaterialCommunityIcons name="magnify" color={color} size={24} />,
        }}
      />
      <Tabs.Screen
        name="account"
        options={{
          title: 'من',
          tabBarIcon: ({ color, focused }) => (
            <MaterialCommunityIcons name={focused ? 'account-circle' : 'account-circle-outline'} color={color} size={24} />
          ),
        }}
      />
    </Tabs>
  );
}

const styles = StyleSheet.create({
  scan: {
    width: 46,
    height: 46,
    marginTop: -18,
    borderRadius: radius.lg,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 4,
    borderColor: colors.card,
    ...shadow.brand,
  },
  scanFocused: { backgroundColor: colors.accentPressed },
});
