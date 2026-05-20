import type { CreateQuoteDto, ValidationError } from "./types.ts";

export function validateCreateQuote(body: CreateQuoteDto): ValidationError | null {
  const errors: Record<string, string[]> = {};

  if (body.author === undefined || body.author === null) {
    errors["author"] = ["The author field is required."];
  } else if (typeof body.author !== "string") {
    errors["author"] = ["The author field must be a string."];
  } else if (body.author.trim().length === 0) {
    errors["author"] = ["The author field must not be empty."];
  } else if (body.author.trim().length > 200) {
    errors["author"] = ["The author field must not exceed 200 characters."];
  }

  if (body.text === undefined || body.text === null) {
    errors["text"] = ["The text field is required."];
  } else if (typeof body.text !== "string") {
    errors["text"] = ["The text field must be a string."];
  } else if (body.text.trim().length === 0) {
    errors["text"] = ["The text field must not be empty."];
  } else if (body.text.trim().length > 2000) {
    errors["text"] = ["The text field must not exceed 2000 characters."];
  }

  if (Object.keys(errors).length > 0) {
    return {
      type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
      title: "One or more validation errors occurred.",
      status: 400,
      errors,
    };
  }

  return null;
}

export function validatePaginationParams(
  pageStr: string | null,
  sizeStr: string | null
): { page: number; size: number; error: ValidationError | null } {
  const errors: Record<string, string[]> = {};

  let page = 1;
  let size = 10;

  if (pageStr !== null) {
    const parsed = Number(pageStr);
    if (!Number.isInteger(parsed) || parsed < 1) {
      errors["page"] = ["Page must be a positive integer."];
    } else {
      page = parsed;
    }
  }

  if (sizeStr !== null) {
    const parsed = Number(sizeStr);
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > 100) {
      errors["size"] = ["Size must be an integer between 1 and 100."];
    } else {
      size = parsed;
    }
  }

  if (Object.keys(errors).length > 0) {
    return {
      page,
      size,
      error: {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        title: "One or more validation errors occurred.",
        status: 400,
        errors,
      },
    };
  }

  return { page, size, error: null };
}
