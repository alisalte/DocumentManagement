import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { listTasks } from '../../src/api/workflow.api';
import { isDemoPreview } from '../../src/config/demo';
import { Text } from '../../src/components/AppText';
import { DocumentRow } from '../../src/components/DocumentRow';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { DottedFill } from '../../src/components/PaperBackground';
import { PageHeader } from '../../src/components/PageHeader';
import { colors, space } from '../../src/theme';
import { userMessage } from '../../src/utils/errors';

export default function TasksScreen() {
  const router = useRouter();
  const tasks = useQuery({ queryKey: ['workflow-tasks'], queryFn: listTasks, enabled: !isDemoPreview() });
  const pending = (tasks.data ?? []).filter((task) => task.status === 'Pending');

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe}>
        <PageHeader title="کارهای من" onBack={() => router.back()} />
        {tasks.error ? (
          <View style={styles.pad}>
            <ErrorMessage message={userMessage(tasks.error)} />
          </View>
        ) : null}
        <FlatList
          data={pending}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={tasks.isRefetching} onRefresh={() => void tasks.refetch()} />}
          ListEmptyComponent={
            tasks.isLoading ? (
              <ActivityIndicator color={colors.accent} style={{ marginTop: 40 }} />
            ) : (
              <EmptyState title="کار معلقی نیست" message="وقتی سندی برای تأیید یا اقدام به شما برسد، اینجا دیده می‌شود." />
            )
          }
          renderItem={({ item }) => (
            <View style={styles.item}>
              <DocumentRow
                title={item.documentTitle}
                subtitle={item.stepName}
                updatedAt={item.dueAt ?? item.createdAt}
                badge={item.isOverdue ? 'دیرکرد' : item.versionLabel}
                onPress={() => router.push({ pathname: '/(app)/document', params: { id: item.documentId } })}
              />
              {item.canAct ? <Text style={styles.hint}>برای اقدام کامل، از نسخه وب استفاده کنید.</Text> : null}
            </View>
          )}
        />
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  list: { padding: space.md, gap: space.md, flexGrow: 1 },
  pad: { paddingHorizontal: space.md },
  item: { gap: space.xs },
  hint: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
    paddingHorizontal: 4,
  },
});
