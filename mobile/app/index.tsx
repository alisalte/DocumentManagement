import { Redirect } from 'expo-router';
import { Loading } from '../src/components/Loading';
import { useAuth } from '../src/auth/AuthProvider';

export default function Index() {
  const { isLoading, isAuthenticated, user } = useAuth();
  if (isLoading) return <Loading />;
  if (!isAuthenticated) return <Redirect href="/(auth)/login" />;
  if (user?.mustChangePassword) return <Redirect href="/(auth)/change-password" />;
  return <Redirect href="/(app)/home" />;
}
