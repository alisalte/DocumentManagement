import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { changePassword } from '../../src/api/auth.api';
import { useAuth } from '../../src/auth/AuthProvider';
import { BrandMark } from '../../src/components/BrandMark';
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
          <View style={styles.hero}>
            <BrandMark size={56} />
            <Text style={styles.title}>رمز عبور را عوض کنید</Text>
            <Text style={styles.subtitle}>تا وقتی رمز را عوض نکنید، سامانه اجازهٔ ثبت سند نمی‌دهد.</Text>
          </View>
          <View style={styles.form}>
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
          </View>
          <Button label="ثبت رمز جدید" onPress={form.handleSubmit(onSubmit)} loading={form.formState.isSubmitting} style={styles.submit} />
          <Button label="خروج" variant="ghost" onPress={() => void logout()} />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  content: { padding: space.lg, paddingBottom: space.xl, gap: space.md },
  hero: { alignItems: 'center', gap: space.sm, marginTop: space.md },
  title: { fontSize: 28, fontWeight: '800', color: colors.ink, textAlign: 'center', writingDirection: 'rtl' },
  subtitle: { color: colors.muted, textAlign: 'center', writingDirection: 'rtl', fontSize: 16, lineHeight: 26 },
  form: { gap: space.md, marginTop: space.md },
  submit: { marginTop: space.sm },
});
