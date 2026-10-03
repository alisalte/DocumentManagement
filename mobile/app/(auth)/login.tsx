import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { BrandMark } from '../../src/components/BrandMark';
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
          <View style={styles.hero}>
            <BrandMark />
            <Text style={styles.title}>به اسکنر اسناد خوش آمدید</Text>
            <Text style={styles.subtitle}>با همان حساب سامانهٔ بایگانی وارد شوید و سند را از گوشی ثبت کنید.</Text>
          </View>
          <View style={styles.form}>
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
          </View>
          <View style={styles.spacer} />
          <Button label="ورود" onPress={form.handleSubmit(onSubmit)} loading={form.formState.isSubmitting} />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  content: { flexGrow: 1, padding: space.lg, paddingBottom: space.xl },
  hero: { alignItems: 'center', gap: space.sm, marginTop: space.lg },
  title: {
    fontSize: 28,
    fontWeight: '800',
    color: colors.ink,
    textAlign: 'center',
    writingDirection: 'rtl',
    marginTop: space.md,
  },
  subtitle: {
    color: colors.muted,
    textAlign: 'center',
    writingDirection: 'rtl',
    fontSize: 16,
    lineHeight: 26,
    paddingHorizontal: space.sm,
  },
  form: { gap: space.md, marginTop: space.xl },
  spacer: { flexGrow: 1, minHeight: space.lg },
});
