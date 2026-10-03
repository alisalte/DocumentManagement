/** ASP.NET ProblemDetails plus the DMS `code` and `errors` extensions. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}
