import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery } from '@tanstack/react-query';
import { Redirect, useLocalSearchParams, useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { createDocument, getSchema, listCategories, listDocumentTypes } from '../../src/api/documents.api';
import { uploadFile } from '../../src/api/uploads.api';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { Input } from '../../src/components/Input';
import { Loading } from '../../src/components/Loading';
import { DottedFill } from '../../src/components/PaperBackground';
import { PageHeader } from '../../src/components/PageHeader';
import { fileScan, type FilingFormat, type FilingPhase } from '../../src/features/scanner/filing';
import { metadataErrors, requiredFields, toMetadata, unsupportedRequiredFields } from '../../src/features/scanner/metadata';
import { prepareScanFile } from '../../src/features/scanner/prepare-file';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, radius, space } from '../../src/theme';
import type { FieldSchema, UploadResult } from '../../src/types/document';
import { faDigits, newIdempotencyKey } from '../../src/utils/format';
import { userMessage } from '../../src/utils/errors';
import { filingSchema, type FilingValues } from '../../src/utils/validation';

const steps: { id: FilingPhase; label: string }[] = [
  { id: 'prepare', label: 'آماده‌سازی فایل...' },
  { id: 'upload', label: 'در حال آپلود...' },
  { id: 'create', label: 'در حال ثبت سند...' },
];

function resolveFormat(raw: string | string[] | undefined): FilingFormat {
  const value = Array.isArray(raw) ? raw[0] : raw;
  return value === 'image' ? 'image' : 'pdf';
}

export default function UploadScreen() {
  const router = useRouter();
  const session = useScanSession();
  const params = useLocalSearchParams<{ format?: string }>();
  const format = resolveFormat(params.format);
  const form = useForm<FilingValues>({
    resolver: zodResolver(filingSchema),
    defaultValues: { title: 'سند اسکن‌شده', description: '', categoryId: '', documentTypeId: '' },
  });
  const documentTypeId = form.watch('documentTypeId');
  const categories = useQuery({ queryKey: ['categories'], queryFn: listCategories });
  const types = useQuery({ queryKey: ['document-types'], queryFn: listDocumentTypes });
  const schema = useQuery({
    queryKey: ['document-type-schema', documentTypeId],
    queryFn: () => getSchema(documentTypeId),
    enabled: documentTypeId.length > 0,
  });

  const [metadataValues, setMetadataValues] = useState<Record<string, string>>({});
  const [metadataProblems, setMetadataProblems] = useState<Record<string, string>>({});
  const [phase, setPhase] = useState<FilingPhase | null>(null);
  const [progress, setProgress] = useState(0);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [duplicates, setDuplicates] = useState<UploadResult['duplicates']>([]);
  const [preparedUri, setPreparedUri] = useState<string | undefined>(
    session.prepared?.format === format ? session.prepared.uri : undefined,
  );
  const [preparedName, setPreparedName] = useState<string | undefined>(
    session.prepared?.format === format ? session.prepared.fileName : undefined,
  );
  const [preparedMime, setPreparedMime] = useState<string | undefined>(
    session.prepared?.format === format ? session.prepared.mimeType : undefined,
  );
  const [uploadId, setUploadId] = useState<string | undefined>();
  const idempotency = useRef({ signature: '', key: '' });
  const pageKey = session.pages.map((page) => page.id).join(',');
  const seenPages = useRef(`${pageKey}:${format}`);

  useEffect(() => {
    const key = `${pageKey}:${format}`;
    if (seenPages.current === key) return;
    seenPages.current = key;
    setPreparedUri(undefined);
    setPreparedName(undefined);
    setPreparedMime(undefined);
    setUploadId(undefined);
  }, [pageKey, format]);

  useEffect(() => {
    if (form.getValues('documentTypeId') || !types.data) return;
    const active = types.data.filter((type) => type.isActive);
    const general = active.find((type) => type.code === 'GENERAL') ?? active[0];
    if (general) form.setValue('documentTypeId', general.id);
  }, [types.data, form]);

  if (session.pages.length === 0) return <Redirect href="/(app)/scan" />;
  if (format === 'image' && session.pages.length !== 1) {
    return <Redirect href={{ pathname: '/(app)/preview', params: { format: 'image' } }} />;
  }

  const creatable = (categories.data ?? []).filter((category) => category.canCreate && category.isActive);
  const activeTypes = (types.data ?? []).filter((type) => type.isActive);
  const fields = schema.data ? requiredFields(schema.data) : [];
  const blocked = schema.data ? unsupportedRequiredFields(schema.data) : [];

  function signatureOf(values: FilingValues): string {
    return JSON.stringify({ ...values, metadataValues, pageKey, format });
  }

  function idempotencyKey(values: FilingValues): string {
    const signature = signatureOf(values);
    if (idempotency.current.signature !== signature) {
      idempotency.current = { signature, key: newIdempotencyKey() };
    }
    return idempotency.current.key;
  }

  async function submit(values: FilingValues) {
    if (blocked.length > 0) return;
    const problems = metadataErrors(fields, metadataValues);
    setMetadataProblems(problems);
    if (Object.keys(problems).length > 0) return;

    setRunning(true);
    setError(null);
    setDuplicates([]);
    try {
      const outcome = await fileScan(
        {
          prepareFile: prepareScanFile,
          upload: uploadFile,
          createDocument,
        },
        {
          pages: session.pages,
          format,
          fileUri: preparedUri,
          fileName: preparedName,
          mimeType: preparedMime,
          uploadId,
          idempotencyKey: idempotencyKey(values),
          draft: {
            title: values.title.trim(),
            description: values.description.trim() ? values.description.trim() : null,
            categoryId: values.categoryId,
            documentTypeId: values.documentTypeId,
            tags: [],
            changeDescription: format === 'image' ? 'عکس با موبایل' : 'اسکن با موبایل',
            metadata: toMetadata(fields, metadataValues),
          },
          onPhase: setPhase,
          onProgress: setProgress,
          onPrepared: (file) => {
            setPreparedUri(file.uri);
            setPreparedName(file.fileName);
            setPreparedMime(file.mimeType);
            session.setPrepared({ ...file, format });
          },
          onUploaded: setUploadId,
        },
      );
      if (outcome.status === 'duplicate') {
        setDuplicates(outcome.duplicates);
        setPhase(null);
        return;
      }
      router.replace({
        pathname: '/(app)/document-success',
        params: {
          documentId: outcome.document.documentId,
          versionId: outcome.document.versionId,
          label: outcome.document.label,
          title: values.title.trim(),
        },
      });
    } catch (caught) {
      setError(userMessage(caught));
    } finally {
      setRunning(false);
    }
  }

  const showProgress = running || phase !== null || error !== null;
  const formatLabel = format === 'image' ? 'عکس' : 'PDF';

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe}>
        <PageHeader title={`ثبت سند (${formatLabel})`} onBack={running ? undefined : () => router.back()} />
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {showProgress ? (
            <Card>
              <View style={styles.track}>
                <View style={[styles.trackFill, { width: `${trackPercent(phase, progress)}%` }]} />
              </View>
              {steps.map((step) => (
                <Text key={step.id} style={[styles.step, phase === step.id ? styles.stepActive : null]}>
                  {step.id === 'prepare' && format === 'image' ? 'آماده‌سازی عکس...' : step.label}
                  {step.id === 'upload' && phase === 'upload' ? ` ${faDigits(Math.round(progress * 100))}٪` : ''}
                </Text>
              ))}
              <ErrorMessage message={error} />
              {error ? <Button label="تلاش دوباره" onPress={form.handleSubmit(submit)} loading={running} /> : null}
              {running ? <Loading label={steps.find((step) => step.id === phase)?.label ?? 'در حال انجام...'} /> : null}
            </Card>
          ) : null}

          {!running ? (
            <View style={styles.form}>
              <Text style={styles.formatHint}>
                {format === 'image'
                  ? 'فایل نهایی همان عکس گرفته‌شده است و به PDF تبدیل نمی‌شود.'
                  : 'صفحات به‌صورت یک فایل PDF ثبت می‌شوند.'}
              </Text>
              <ErrorMessage message={categories.error ? userMessage(categories.error) : null} />
              <ErrorMessage message={types.error ? userMessage(types.error) : null} />
              <ErrorMessage message={schema.error ? userMessage(schema.error) : null} />
              {duplicates.length > 0 ? (
                <Card>
                  <Text style={styles.blockTitle}>این فایل قبلاً ثبت شده است</Text>
                  {duplicates.map((item) => (
                    <Text key={item.documentId} style={styles.blockText}>
                      {item.title}
                    </Text>
                  ))}
                </Card>
              ) : null}
              <Controller
                control={form.control}
                name="title"
                render={({ field, fieldState }) => (
                  <Input label="عنوان" value={field.value} onChangeText={field.onChange} onBlur={field.onBlur} error={fieldState.error?.message} />
                )}
              />
              <Controller
                control={form.control}
                name="description"
                render={({ field, fieldState }) => (
                  <Input
                    label="توضیح (اختیاری)"
                    value={field.value}
                    onChangeText={field.onChange}
                    onBlur={field.onBlur}
                    multiline
                    error={fieldState.error?.message}
                  />
                )}
              />
              <Text style={styles.section}>پوشه</Text>
              {categories.isLoading ? <Loading label="در حال دریافت پوشه‌ها..." /> : null}
              {creatable.length === 0 && categories.isSuccess ? (
                <Text style={styles.blockText}>پوشه‌ای با اجازهٔ ایجاد سند برای شما وجود ندارد.</Text>
              ) : null}
              {creatable.map((category) => (
                <Choice
                  key={category.id}
                  label={'  '.repeat(category.depth) + category.name}
                  selected={form.watch('categoryId') === category.id}
                  onPress={() => form.setValue('categoryId', category.id, { shouldValidate: true })}
                />
              ))}
              {form.formState.errors.categoryId ? <Text style={styles.fieldError}>{form.formState.errors.categoryId.message}</Text> : null}

              <Text style={styles.section}>نوع سند</Text>
              {activeTypes.map((type) => (
                <Choice
                  key={type.id}
                  label={type.name}
                  selected={documentTypeId === type.id}
                  onPress={() => {
                    form.setValue('documentTypeId', type.id, { shouldValidate: true });
                    setMetadataValues({});
                  }}
                />
              ))}
              {form.formState.errors.documentTypeId ? (
                <Text style={styles.fieldError}>{form.formState.errors.documentTypeId.message}</Text>
              ) : null}

              {schema.isLoading ? <Loading label="در حال دریافت فرم سند..." /> : null}
              {blocked.length > 0 ? (
                <ErrorMessage message="این نوع سند فیلد الزامی دارد که در نسخهٔ اول موبایل پشتیبانی نمی‌شود. نوع «سند عمومی» را انتخاب کنید." />
              ) : null}
              {fields.map((field) => (
                <MetadataField
                  key={field.code}
                  field={field}
                  value={metadataValues[field.code] ?? ''}
                  error={metadataProblems[field.code]}
                  onChange={(value) => setMetadataValues((current) => ({ ...current, [field.code]: value }))}
                />
              ))}

              <Button
                label="ثبت سند"
                onPress={form.handleSubmit(submit)}
                loading={running}
                disabled={blocked.length > 0 || schema.isLoading || !schema.data}
              />
            </View>
          ) : null}
        </ScrollView>
      </SafeAreaView>
    </DottedFill>
  );
}

function Choice({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={[styles.choice, selected ? styles.choiceOn : null]}>
      <Text style={[styles.choiceText, selected ? styles.choiceTextOn : null]}>{label}</Text>
    </Pressable>
  );
}

function trackPercent(phase: FilingPhase | null, progress: number): number {
  if (phase === 'prepare') return 18;
  if (phase === 'upload') return Math.max(18, Math.min(90, Math.round(progress * 100)));
  if (phase === 'create') return 100;
  return progress > 0 ? Math.round(progress * 100) : 8;
}

function MetadataField({
  field,
  value,
  error,
  onChange,
}: {
  field: FieldSchema;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  if (field.type === 'Boolean') {
    return (
      <View style={styles.field}>
        <Text style={styles.section}>{field.label.fa}</Text>
        <View style={styles.row}>
          <Choice label="بله" selected={value === 'true'} onPress={() => onChange('true')} />
          <Choice label="خیر" selected={value === 'false'} onPress={() => onChange('false')} />
        </View>
        {error ? <Text style={styles.fieldError}>{error}</Text> : null}
      </View>
    );
  }
  if (field.type === 'Select') {
    return (
      <View style={styles.field}>
        <Text style={styles.section}>{field.label.fa}</Text>
        {(field.options ?? []).filter((option) => option.isActive).map((option) => (
          <Choice key={option.value} label={option.label.fa} selected={value === option.value} onPress={() => onChange(option.value)} />
        ))}
        {error ? <Text style={styles.fieldError}>{error}</Text> : null}
      </View>
    );
  }
  return (
    <Input
      label={field.label.fa}
      value={value}
      onChangeText={onChange}
      error={error}
      keyboardType={field.type === 'Integer' || field.type === 'Decimal' || field.type === 'Phone' ? 'numeric' : 'default'}
      autoCapitalize={field.type === 'Email' || field.type === 'Url' ? 'none' : 'sentences'}
      placeholder={field.type === 'Date' ? 'YYYY-MM-DD' : undefined}
    />
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, gap: space.md, paddingBottom: space.xl },
  form: { gap: space.sm },
  track: { height: 8, borderRadius: radius.pill, backgroundColor: colors.line, overflow: 'hidden' },
  trackFill: { height: '100%', borderRadius: radius.pill, backgroundColor: colors.accent },
  step: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl' },
  stepActive: { color: colors.ink, fontWeight: '700' },
  section: { color: colors.ink, fontSize: 16, fontWeight: '800', textAlign: 'right', writingDirection: 'rtl', marginTop: space.sm },
  choice: {
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingVertical: 14,
    paddingHorizontal: space.md,
    backgroundColor: colors.card,
  },
  choiceOn: { borderColor: colors.accent, backgroundColor: colors.accentSoft },
  choiceText: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl', fontSize: 15 },
  choiceTextOn: { color: colors.accent, fontWeight: '700' },
  fieldError: { color: colors.danger, textAlign: 'right', writingDirection: 'rtl' },
  blockTitle: { color: colors.ink, fontWeight: '800', textAlign: 'right', writingDirection: 'rtl' },
  blockText: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl' },
  formatHint: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl', marginBottom: space.xs },
  field: { gap: space.xs },
  row: { flexDirection: 'row', gap: space.sm },
});
