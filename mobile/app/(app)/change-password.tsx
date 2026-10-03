import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { changePassword } from '../../src/api/auth.api';
import { useAuth } from '../../src/auth/AuthProvider';
import { Text } from '../../src/components/AppText';
import { AuthField } from '../../src/components/AuthField';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { DottedFill } from '../../src/components/PaperBackground';
import { PageHeader } from '../../src/components/PageHeader';
import { colors, space } from '../../src/theme';
import { ApiError, userMessage } from '../../src/utils/errors';
import { passwordSchema, type PasswordValues } from '../../src/utils/validation';

export default function AppChangePasswordScreen() {
  const { clearLocal } = useAuth();
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
    <DottedFill>
      <SafeAreaView style={styles.safe}>
        <PageHeader title="تغییر رمز عبور" onBack={() => router.back()} />
        <View style={styles.content}>
          <Text style={styles.note}>بعد از تغییر رمز، دوباره وارد شوید. نشست‌های قبلی بسته می‌شوند.</Text>
          <ErrorMessage message={error} />
          <Controller
            control={form.control}
            name="currentPassword"
            render={({ field, fieldState }) => (
              <AuthField
                label="رمز فعلی"
                secureTextEntry
                returnKeyType="next"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
              />
            )}
          />
          <Controller
            control={form.control}
            name="newPassword"
            render={({ field, fieldState }) => (
              <AuthField
                label="رمز جدید"
                secureTextEntry
                returnKeyType="next"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
              />
            )}
          />
          <Controller
            control={form.control}
            name="confirmPassword"
            render={({ field, fieldState }) => (
              <AuthField
                label="تکرار رمز جدید"
                secureTextEntry
                returnKeyType="done"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
              />
            )}
          />
          <Button
            label="ثبت رمز جدید"
            loading={form.formState.isSubmitting}
            onPress={form.handleSubmit(onSubmit)}
          />
        </View>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, gap: space.sm },
  note: {
    color: colors.muted,
    textAlign: 'right',
    writingDirection: 'rtl',
    fontSize: 14,
    lineHeight: 22,
    marginBottom: space.xs,
  },
});
