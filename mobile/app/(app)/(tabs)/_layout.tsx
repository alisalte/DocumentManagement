import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Tabs } from 'expo-router';
import { colors } from '../../../src/theme';

export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.accent,
        tabBarInactiveTintColor: colors.muted,
        tabBarStyle: {
          backgroundColor: colors.card,
          borderTopColor: colors.line,
          height: 64,
          paddingBottom: 8,
          paddingTop: 6,
        },
        tabBarLabelStyle: {
          fontFamily: 'Vazir',
          fontSize: 11,
          writingDirection: 'rtl',
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'بایگانی',
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="folder-outline" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="capture"
        options={{
          title: 'اسکن',
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="scanner" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="search"
        options={{
          title: 'جستجو',
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="magnify" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="account"
        options={{
          title: 'من',
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="account-outline" color={color} size={size} />,
        }}
      />
    </Tabs>
  );
}
