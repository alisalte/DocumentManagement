import { Redirect } from 'expo-router';

/** Kept so old links still land on the tabbed home. */
export default function HomeRedirect() {
  return <Redirect href="/(app)/(tabs)" />;
}
