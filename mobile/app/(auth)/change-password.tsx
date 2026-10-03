import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { changePassword } from '../../src/api/auth.api';
import { useAuth } from '../../src/auth/AuthProvider';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { Input } from '../../src/components/Input';
import { colors, space } from '../../src/theme';
import { ApiError, userMessage } from '../../src/utils/errors';
import { passwordSchema, type PasswordValues } from '../../src/utils/validation';

export default function ChangePasswordScreen() {
  const { clearLocal, logout } = useAuth();
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<PasswordValues>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });

  async function onSubmit(values: PasswordValues) {
    setError(null);
    try {
      await changePassword(values.currentPassword, values.newPassword);
      await clearLocal();
      router.replace('/(auth)/login');
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'auth.invalid_credentials') {
        setError('رمز فعلی نادرست است.');
        return;
      }
      setError(userMessage(caught));
    }
  }

  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.title}>تغییر رمز عبور</Text>
          <Text style={styles.subtitle}>تا وقتی رمز را عوض نکنید، سامانه اجازهٔ ثبت سند نمی‌دهد.</Text>
          <ErrorMessage message={error} />
          <Controller
            control={form.control}
            name="currentPassword"
            render={({ field, fieldState }) => (
              <Input label="رمز فعلی" secureTextEntry value={field.value} onChangeText={field.onChange} onBlur={field.onBlur} error={fieldState.error?.message} />
            )}
          />
          <Controller
            control={form.control}
            name="newPassword"
            render={({ field, fieldState }) => (
              <Input label="رمز جدید" secureTextEntry value={field.value} onChangeText={field.onChange} onBlur={field.onBlur} error={fieldState.error?.message} />
            )}
          />
          <Controller
            control={form.control}
            name="confirmPassword"
            render={({ field, fieldState }) => (
              <Input label="تکرار رمز جدید" secureTextEntry value={field.value} onChangeText={field.onChange} onBlur={field.onBlur} error={fieldState.error?.message} />
            )}
          />
          <Button label="ثبت رمز جدید" onPress={form.handleSubmit(onSubmit)} loading={form.formState.isSubmitting} />
          <Button label="خروج" variant="ghost" onPress={() => void logout()} />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  content: { padding: space.lg, gap: space.md },
  title: { fontSize: 24, fontWeight: '700', color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  subtitle: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl' },
});
