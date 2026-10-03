import { Redirect, Stack } from 'expo-router';
import { Loading } from '../../src/components/Loading';
import { useAuth } from '../../src/auth/AuthProvider';
import { ScanSessionProvider } from '../../src/features/scanner/ScanSessionProvider';

export default function AppLayout() {
  const { isLoading, isAuthenticated, user } = useAuth();
  if (isLoading) return <Loading />;
  if (!isAuthenticated) return <Redirect href="/(auth)/login" />;
  if (user?.mustChangePassword) return <Redirect href="/(auth)/change-password" />;
  return (
    <ScanSessionProvider>
      <Stack screenOptions={{ headerShown: false }} />
    </ScanSessionProvider>
  );
}
