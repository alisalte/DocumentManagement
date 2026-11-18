import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Platform, StyleSheet, View } from 'react-native';
import { Button } from 'react-native-paper';
import { useAuth } from '../../src/auth/AuthProvider';
import { Text } from '../../src/components/AppText';
import { AuthField } from '../../src/components/AuthField';
import { BrandMark } from '../../src/components/BrandMark';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { PaperBackground } from '../../src/components/PaperBackground';
import { currentApiUrl, saveServerUrl } from '../../src/config/server-url-store';
import { paperColors } from '../../src/theme/paper';
import { colors, radius, shadow } from '../../src/theme';
import { userMessage } from '../../src/utils/errors';
import { loginSchema, type LoginValues } from '../../src/utils/validation';

const SERVER_HINT = 'مثال: http://62.60.166.71:8090';

export default function LoginScreen() {
  const { login } = useAuth();
  const [error, setError] = useState<string | null>(null);
  const [server, setServer] = useState(() => (Platform.OS === 'web' ? '' : currentApiUrl()));
  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: '', password: '' },
  });

  async function onSubmit(values: LoginValues) {
    setError(null);
    try {
      if (Platform.OS !== 'web') await saveServerUrl(server);
      await login(values.username, values.password);
    } catch (caught) {
      if (caught instanceof Error && /server url/.test(caught.message)) {
        setError('آدرس سرور معتبر نیست. آدرس صفحه ورود بایگانی را وارد کنید، مثل http://192.168.1.10:8090 (نه پورت ۸۰۹۱ اسکنر وب).');
        return;
      }
      setError(userMessage(caught));
    }
  }

  return (
    <PaperBackground>
      <BrandMark size={72} />
      <Text style={styles.header}>ورود به حساب</Text>
      <Text style={styles.sub}>با نام کاربری و رمز سازمانی خود وارد بایگانی شوید.</Text>
      <View style={styles.card}>
      <View style={styles.full}>
        <ErrorMessage message={error} />
      </View>
      {Platform.OS !== 'web' && (
        <>
          <AuthField
            label="آدرس سرور"
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="url"
            textContentType="URL"
            returnKeyType="next"
            placeholder={SERVER_HINT}
            value={server}
            onChangeText={setServer}
            onBlur={() => undefined}
          />
          <Text style={styles.hint}>
            همان آدرس صفحه ورود بایگانی (معمولاً پورت ۸۰۹۰). اگر آدرس اسکنر وب (۸۰۹۱) را بگذارید، برنامه خودش به ۸۰۹۰ عوضش می‌کند.
          </Text>
        </>
      )}
      <Controller
        control={form.control}
        name="username"
        render={({ field, fieldState }) => (
          <AuthField
            label="نام کاربری"
            autoCapitalize="none"
            autoCorrect={false}
            textContentType="username"
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
        name="password"
        render={({ field, fieldState }) => (
          <AuthField
            label="رمز عبور"
            secureTextEntry
            textContentType="password"
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
        ورود
      </Button>
      </View>
      <Text style={styles.footer}>امن · قابل حسابرسی · فارسی</Text>
    </PaperBackground>
  );
}

const styles = StyleSheet.create({
  header: {
    fontSize: 24,
    fontWeight: '800',
    color: colors.ink,
    textAlign: 'center',
    writingDirection: 'rtl',
    marginTop: 18,
  },
  sub: {
    fontSize: 14,
    lineHeight: 22,
    color: colors.muted,
    textAlign: 'center',
    writingDirection: 'rtl',
    marginTop: 4,
    marginBottom: 20,
  },
  card: {
    width: '100%',
    backgroundColor: colors.card,
    borderRadius: radius.lg + 4,
    borderWidth: 1,
    borderColor: colors.line,
    padding: 18,
    ...shadow.raised,
  },
  footer: { marginTop: 20, color: colors.subtle, fontSize: 12, writingDirection: 'rtl' },
  hint: {
    width: '100%',
    color: colors.muted,
    fontSize: 12,
    lineHeight: 20,
    textAlign: 'right',
    writingDirection: 'rtl',
    marginTop: -4,
    marginBottom: 8,
  },
  button: { width: '100%', borderRadius: radius.md, marginTop: 14, ...shadow.brand },
  buttonContent: { height: 50 },
  buttonLabel: { fontFamily: 'Vazir-Bold', fontSize: 15, fontWeight: 'normal' },
  full: { width: '100%' },
});
