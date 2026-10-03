import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { Input } from '../../src/components/Input';
import { useAuth } from '../../src/auth/AuthProvider';
import { colors, space } from '../../src/theme';
import { ApiError, userMessage } from '../../src/utils/errors';
import { loginSchema, type LoginValues } from '../../src/utils/validation';

export default function LoginScreen() {
  const { login } = useAuth();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: '', password: '' },
  });

  async function onSubmit(values: LoginValues) {
    setError(null);
    try {
      await login(values.username, values.password);
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'auth.account_locked') {
        setError(userMessage(caught));
        return;
      }
      setError(userMessage(caught));
    }
  }

  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.title}>اسکنر اسناد</Text>
          <Text style={styles.subtitle}>با همان حساب سامانهٔ بایگانی وارد شوید.</Text>
          <ErrorMessage message={error} />
          <Controller
            control={form.control}
            name="username"
            render={({ field, fieldState }) => (
              <Input
                label="نام کاربری"
                autoCapitalize="none"
                autoCorrect={false}
                textContentType="username"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
              />
            )}
          />
          <Controller
            control={form.control}
            name="password"
            render={({ field, fieldState }) => (
              <Input
                label="رمز عبور"
                secureTextEntry
                textContentType="password"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
              />
            )}
          />
          <Button label="ورود" onPress={form.handleSubmit(onSubmit)} loading={form.formState.isSubmitting} />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  content: { padding: space.lg, gap: space.md, justifyContent: 'center', flexGrow: 1 },
  title: { fontSize: 28, fontWeight: '700', color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  subtitle: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl', marginBottom: space.sm },
});
