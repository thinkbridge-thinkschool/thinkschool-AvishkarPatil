export interface Quote {
  id: number;
  author: string;
  text: string;
  created_at: string;
}

export interface CreateQuoteDto {
  author?: unknown;
  text?: unknown;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  size: number;
  totalCount: number;
  totalPages: number;
}

export interface ValidationError {
  type: string;
  title: string;
  status: number;
  errors: Record<string, string[]>;
}

export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail?: string;
}
