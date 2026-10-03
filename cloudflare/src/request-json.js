import { HttpError } from './http-error.js';

export async function readJson(request) {
  let body;
  try { body = await request.json(); }
  catch (error) {
    if (!(error instanceof SyntaxError)) throw error;
    throw new HttpError(400, 'invalid_json', 'Request body must be valid JSON');
  }
  if (!body || typeof body !== 'object' || Array.isArray(body))
    throw new HttpError(400, 'invalid_json', 'Request body must be a JSON object');
  return body;
}
