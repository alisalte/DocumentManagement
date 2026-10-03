import { http, withApi } from './client';
import { toQuery } from './query';
import type { SearchParams, SearchResult } from '../types/search';

export function searchDocuments(params: SearchParams): Promise<SearchResult> {
  return withApi(async () => (await http.get<SearchResult>(`/search${toQuery({ ...params })}`)).data);
}
