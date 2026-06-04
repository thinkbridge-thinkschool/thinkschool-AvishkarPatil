// Auth contract — POST /api/auth/login (QuotesApi/Extensions/AuthEndpointExtensions.cs).
//   request  { email, password }     (LoginRequest)
//   response { accessToken, refreshToken, expiresIn }   (LoginResponse, camelCase)
// Only accessToken is used here — it carries the scope=quotes.write claim for a
// "writer" user, which the can-edit-quotes policy on POST /api/quotes requires.

export interface LoginResponse {
  accessToken:  string;
  refreshToken: string;
  expiresIn:    number;
}
