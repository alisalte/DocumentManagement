import { Redirect, Stack } from 'expo-router';
import { Loading } from '../../src/components/Loading';
import { useAuth } from '../../src/auth/AuthProvider';

export default function AuthLayout() {
  const { isLoading, isAuthenticated, user } = useAuth();
  if (isLoading) return <Loading />;
  if (isAuthenticated && !user?.mustChangePassword) return <Redirect href="/(app)/(tabs)" />;
  return <Stack screenOptions={{ headerShown: false }} />;
}
