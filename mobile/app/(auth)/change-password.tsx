import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from 'react-native-paper';
import { changePassword } from '../../src/api/auth.api';
import { useAuth } from '../../src/auth/AuthProvider';
import { Text } from '../../src/components/AppText';
import { AuthField } from '../../src/components/AuthField';
import { BrandMark } from '../../src/components/BrandMark';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { PaperBackground } from '../../src/components/PaperBackground';
import { paperColors } from '../../src/theme/paper';
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
    <PaperBackground>
      <BrandMark size={96} />
      <Text style={styles.header}>تغییر رمز عبور</Text>
      <Text style={styles.note}>تا وقتی رمز را عوض نکنید، سامانه اجازهٔ ثبت سند نمی‌دهد.</Text>
      <View style={styles.full}>
        <ErrorMessage message={error} />
      </View>
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
        mode="contained"
        buttonColor={paperColors.primary}
        textColor="#ffffff"
        style={styles.button}
        contentStyle={styles.buttonContent}
        labelStyle={styles.buttonLabel}
        loading={form.formState.isSubmitting}
        disabled={form.formState.isSubmitting}
        onPress={form.handleSubmit(onSubmit)}
      >
        ثبت رمز جدید
      </Button>
      <Pressable accessibilityRole="button" onPress={() => void logout()} style={styles.logout}>
        <Text style={styles.logoutText}>خروج</Text>
      </Pressable>
    </PaperBackground>
  );
}

const styles = StyleSheet.create({
  header: {
    fontSize: 26,
    fontWeight: '700',
    color: paperColors.primary,
    textAlign: 'center',
    writingDirection: 'rtl',
    paddingTop: 14,
  },
  note: {
    color: paperColors.secondary,
    textAlign: 'center',
    writingDirection: 'rtl',
    fontSize: 15,
    lineHeight: 24,
    marginBottom: 8,
  },
  button: { width: '100%', borderRadius: 4, marginTop: 12 },
  buttonContent: { height: 48 },
  buttonLabel: { fontFamily: 'Vazir-Bold', fontSize: 15, fontWeight: 'normal' },
  logout: { marginTop: 8, padding: 8 },
  logoutText: { color: paperColors.secondary, fontSize: 15, writingDirection: 'rtl' },
  full: { width: '100%' },
});
