/**
 * Mailpit (dev SMTP catcher, `docker/docker-compose.yml`) HTTP API helpers -- used by Task
 * 15.16's registration/email-verification spec to fetch the confirmation link
 * `SmtpEmailSender.SendConfirmationLinkAsync` (backend/IdentityService/Services/
 * SmtpEmailSender.cs) actually sent, the same way a real user would read it from their inbox,
 * rather than reaching into IdentityService's database for the raw token.
 *
 * API reference: https://mailpit.axllent.org/docs/api-v1/ -- `GET /api/v1/search` supports the
 * same query syntax as the Mailpit web UI's search box (e.g. `to:"someone@example.com"`).
 */

export const MAILPIT_URL = process.env.MAILPIT_URL ?? "http://localhost:8025";

interface MailpitMessageSummary {
  ID: string;
}

interface MailpitSearchResponse {
  messages: MailpitMessageSummary[];
}

interface MailpitMessageDetail {
  HTML: string;
  Text: string;
}

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${MAILPIT_URL}${path}`);
  if (!res.ok) {
    throw new Error(`Mailpit GET ${path} failed with status ${res.status}`);
  }
  return (await res.json()) as T;
}

/**
 * The most recent message sent to `email` (Mailpit's search results are newest-first), polling
 * briefly since SMTP delivery from IdentityService -> Mailpit is asynchronous relative to the
 * HTTP request that triggered it (registration's `OnPostAsync` fires-and-forgets the send inside
 * a try/catch -- see its remarks -- so the message may not have landed the instant the browser
 * navigates back to this app). Throws if nothing arrives within the timeout, failing the calling
 * spec loudly rather than silently proceeding without a confirmation link.
 */
async function findLatestMessageTo(email: string, timeoutMs = 15_000): Promise<MailpitMessageSummary> {
  const query = encodeURIComponent(`to:"${email}"`);
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const result = await getJson<MailpitSearchResponse>(`/api/v1/search?query=${query}`);
    if (result.messages.length > 0) {
      return result.messages[0];
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`No Mailpit message arrived for "${email}" within ${timeoutMs}ms.`);
}

/**
 * Fetches the newest email sent to `email` and extracts the `<a href="...ConfirmEmail...">`
 * link from its HTML body -- the exact link shape `Register/Index.cshtml.cs` builds
 * (`{scheme}://{host}/Account/ConfirmEmail?userId=...&code=...`). Entities are unescaped
 * (`&amp;` -> `&`) since the HTML body encodes the querystring's `&` separator as an entity.
 */
export async function fetchConfirmationLink(email: string): Promise<string> {
  const message = await findLatestMessageTo(email);
  const detail = await getJson<MailpitMessageDetail>(`/api/v1/message/${message.ID}`);

  const match = detail.HTML.match(/href="([^"]*\/Account\/ConfirmEmail\?[^"]+)"/);
  if (!match) {
    throw new Error(`Confirmation link not found in message body for "${email}": ${detail.HTML}`);
  }

  return match[1].replace(/&amp;/g, "&");
}
