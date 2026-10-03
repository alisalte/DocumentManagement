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
import { userMessage } from '../../src/utils/errors';
import { loginSchema, type LoginValues } from '../../src/utils/validation';

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
        setError('آدرس سرور معتبر نیست. همان آدرس صفحه ورود را وارد کنید، مثل http://192.168.1.10:8090');
        return;
      }
      setError(userMessage(caught));
    }
  }

  return (
    <PaperBackground>
      <BrandMark size={112} />
      <Text style={styles.header}>خوش آمدید.</Text>
      <View style={styles.full}>
        <ErrorMessage message={error} />
      </View>
      {Platform.OS !== 'web' && (
        <AuthField
          label="آدرس سرور"
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
          textContentType="URL"
          returnKeyType="next"
          value={server}
          onChangeText={setServer}
          onBlur={() => undefined}
        />
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
    paddingVertical: 14,
  },
  button: { width: '100%', borderRadius: 4, marginTop: 12, marginBottom: 8 },
  buttonContent: { height: 48 },
  buttonLabel: { fontFamily: 'Vazir-Bold', fontSize: 15, fontWeight: 'normal' },
  full: { width: '100%' },
});
