import { HTTPException } from 'hono/http-exception';

export class InvalidPlayerCredentialsError extends HTTPException {
  constructor() {
    super(401, { message: 'invalid player credentials' });
  }
}
