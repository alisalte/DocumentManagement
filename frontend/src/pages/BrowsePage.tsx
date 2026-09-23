import {
  Alert,
  Box,
  Button,
  FormControlLabel,
  LinearProgress,
  Pagination,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { DocumentList } from '../components/DocumentList';
import { api } from '../lib/api';
import { formatNumber } from '../lib/format';
import { describeError, t } from '../strings';

const pageSize = 25;

export function BrowsePage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const categoryId = params.get('category');
  const page = Number(params.get('page') ?? '1');
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');
  const [includeSubcategories, setIncludeSubcategories] = useState(true);

  // Search as the user types, but not on every keystroke.
  useEffect(() => {
    const handle = setTimeout(() => {
      const next = new URLSearchParams(params);
      // Sent as typed: titles are stored as typed, and folding digits here would miss "۱۴۰۳".
      const value = searchInput.trim();
      if (value) next.set('q', value);
      else next.delete('q');
      next.delete('page');
      if (next.toString() !== params.toString()) setParams(next, { replace: true });
    }, 350);
    return () => clearTimeout(handle);
  }, [searchInput, params, setParams]);

  const search = params.get('q') ?? '';
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const documents = useQuery({
    queryKey: ['documents', categoryId, includeSubcategories, search, page],
    queryFn: () => api.documents({ categoryId, includeSubcategories, search, page, pageSize }),
    placeholderData: keepPreviousData,
  });

  const category = categories.data?.find((item) => item.id === categoryId);
  const heading = category?.name ?? t.allDocuments;
  const pages = documents.data ? Math.max(1, Math.ceil(documents.data.total / pageSize)) : 1;

  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
        <Typography variant="h5" component="h1" sx={{ flexGrow: 1 }}>
          {heading}
          {documents.data && (
            <Typography component="span" color="text.secondary" sx={{ marginInlineStart: 1 }}>
              ({formatNumber(documents.data.total)})
            </Typography>
          )}
        </Typography>
        {category?.canCreate && (
          <Button variant="contained" component={RouterLink} to={`/new?category=${category.id}`}>
            {t.newDocument}
          </Button>
        )}
      </Stack>

      <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
          <TextField
            label={t.search}
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            size="small"
            fullWidth
            type="search"
          />
          {categoryId && (
            <FormControlLabel
              sx={{ flexShrink: 0 }}
              control={
                <Switch
                  checked={includeSubcategories}
                  onChange={(event) => setIncludeSubcategories(event.target.checked)}
                />
              }
              label={t.includeSubcategories}
            />
          )}
        </Stack>
      </Paper>

      <Paper variant="outlined">
        {documents.isFetching && <LinearProgress />}
        {documents.isError ? (
          <Alert severity="error" action={<Button onClick={() => documents.refetch()}>{t.retry}</Button>}>
            {describeError(documents.error)}
          </Alert>
        ) : (
          <Box sx={{ p: { xs: 1, sm: 0 } }}>
            {documents.data && (
              <DocumentList items={documents.data.items} onOpen={(item) => navigate(`/documents/${item.id}`)} />
            )}
          </Box>
        )}
      </Paper>

      {pages > 1 && (
        <Pagination
          sx={{ alignSelf: 'center' }}
          count={pages}
          page={page}
          onChange={(_, value) => {
            const next = new URLSearchParams(params);
            next.set('page', String(value));
            setParams(next);
          }}
        />
      )}
    </Stack>
  );
}
