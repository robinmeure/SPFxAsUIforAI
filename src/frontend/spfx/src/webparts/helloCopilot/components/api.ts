import { AadTokenProvider } from '@microsoft/sp-http-base';
import { WebPartContext } from '@microsoft/sp-webpart-base';
import { resolveEndpoints } from '../../../common/apiEndpoints';

// `text` is answer content; `activity` is a short user-facing progress step; `error` ends the reply;
// `chatId` is sent first and identifies the server-side chat the turn was stored in.
export interface PassageCitation {
  number: number; chunkId: string; documentId: string; title: string;
  sourceType: string; sourceUrl?: string; chunkIndex: number; section?: string; pageNumber?: number;
  quote: string; retrievalMethod: string;
}
export interface RetrievalDetails {
  query: string; scope: string; documentIds: string[]; section?: string; pageNumber?: number;
  passageCount: number; notice?: string;
  method?: 'search' | 'reuse' | 'none';
}
export interface StreamEvent {
  text?: string; activity?: string; error?: string; chatId?: string;
  citations?: PassageCitation[]; retrieval?: RetrievalDetails;
  followUpQuestions?: string[];
}

export interface ChatSummary { id: string; title: string; createdAt: string; updatedAt: string; documentIds?: string[]; }
// `event` messages record changes to the conversation's document set; they are shown, never sent to the model.
export interface ChatMessageDto {
  id: string; role: 'user' | 'assistant' | 'event'; content: string; createdAt: string;
  citations?: PassageCitation[]; retrieval?: RetrievalDetails; skill?: string | null;
  followUpQuestions?: string[];
}
export interface SkillSummary { name: string; description: string; }

/** `extract-and-generate` → `Extract and generate`. */
export const skillLabel = (name: string): string => {
  const words = name.replace(/-/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
};
export interface ChatDetails extends ChatSummary { messages: ChatMessageDto[]; documents: UserDocument[]; }
export interface ChatDocumentsResult { documentIds: string[]; event?: ChatMessageDto | null; }

export type DocumentStatus = 'processing' | 'ready' | 'failed';
export interface UserDocument {
  id: string;
  fileName: string;
  sourceType: 'upload' | 'sharepoint';
  sourceUrl?: string;
  sizeBytes: number;
  status: DocumentStatus;
  error?: string;
  chunkCount?: number;
  createdAt: string;
  updatedAt: string;
}

export const SUPPORTED_EXTENSIONS = ['.pdf', '.docx', '.pptx', '.xlsx', '.txt', '.md', '.html', '.htm'];
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export class ApiError extends Error {
  public constructor(message: string, public readonly status: number, public readonly data: Record<string, unknown> = {}) { super(message); }
}

/** The conversation that already owns a document set, from a 409 response. */
export const conflictOf = (error: unknown): { chatId: string; title: string } | undefined =>
  error instanceof ApiError && error.status === 409 && typeof error.data.conflictChatId === 'string'
    ? { chatId: error.data.conflictChatId, title: String(error.data.conflictTitle ?? '') }
    : undefined;

const readProblem = async (response: Response, fallback: string): Promise<ApiError> => {
  try {
    const problem = await response.json() as Record<string, unknown>;
    const message = typeof problem.detail === 'string' && problem.detail ? problem.detail
      : typeof problem.title === 'string' && problem.title ? problem.title : fallback;
    return new ApiError(message, response.status, problem);
  } catch {
    return new ApiError(fallback, response.status);
  }
};

export class ChatApi {
  private readonly _root?: string;
  private readonly _streamUrl: string;
  public readonly configurationError?: string;
  public get hasCompanions(): boolean { return this._root !== undefined; }

  public constructor(private readonly _context: WebPartContext, apiUrl: string, private readonly _apiResource: string, companionBaseUrl?: string) {
    this._streamUrl = apiUrl.trim();
    try { this._root = resolveEndpoints(apiUrl, companionBaseUrl).companionRoot; }
    catch (error) { this.configurationError = error instanceof Error ? error.message : 'Invalid API configuration.'; }
  }

  public listChats = (): Promise<ChatSummary[]> => this._json('/api/chats');

  public getChat = (id: string): Promise<ChatDetails> => this._json(`/api/chats/${encodeURIComponent(id)}`);

  public deleteChat = (id: string): Promise<void> => this._send('DELETE', `/api/chats/${encodeURIComponent(id)}`);

  public setChatDocuments = (id: string, documentIds: string[]): Promise<ChatDocumentsResult> =>
    this._json(`/api/chats/${encodeURIComponent(id)}/documents`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ documentIds })
    });

  public listDocuments = (): Promise<UserDocument[]> => this._json('/api/documents');

  public listSkills = (): Promise<SkillSummary[]> => this._json('/api/skills');

  public deleteDocument = (id: string): Promise<void> => this._send('DELETE', `/api/documents/${encodeURIComponent(id)}`);

  public uploadDocument = async (file: File): Promise<UserDocument> => {
    const form = new FormData();
    form.append('file', file, file.name);
    return this._json('/api/documents', { method: 'POST', body: form });
  };

  public addSharePointDocument = (driveId: string, itemId: string): Promise<UserDocument> =>
    this._json('/api/documents/sharepoint', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ driveId, itemId })
    });

  public streamReply = async (
    message: string, chatId: string | undefined, signal: AbortSignal, onEvent: (event: StreamEvent) => void,
    documentIds?: string[], skill?: string
  ): Promise<void> => {
    const response = await this._fetchUrl(this._streamUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify({ message, chatId, documentIds, skill }),
      signal
    });
    if (response.status === 404) throw new Error('This conversation no longer exists. Start a new chat.');
    if (!response.ok) throw await readProblem(response, `The API returned an error (${response.status}). Please try again.`);
    if (!response.body) throw new Error('This browser doesn\'t support streaming responses.');
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let done = false;
    let invalidEvent = false;
    while (!done) {
      const result = await reader.read();
      done = result.done;
      buffer += decoder.decode(result.value, { stream: !done });
      const blocks = buffer.split(/\r?\n\r?\n/);
      buffer = blocks.pop() || '';
      for (const block of blocks) {
        const dataLines = block.split(/\r?\n/).filter(line => line.startsWith('data:')).map(line => line.slice(5).replace(/^ /, ''));
        if (!dataLines.length) continue;
        const data = dataLines.join('\n');
        if (data === '[DONE]') { done = true; break; }
        let value: StreamEvent;
        try { value = JSON.parse(data) as StreamEvent; }
        catch { invalidEvent = true; continue; }
        if (value.error) throw new Error(value.error);
        onEvent(value);
      }
    }
    await reader.cancel();
    if (invalidEvent) throw new Error('Part of the reply couldn\'t be read, so it may be incomplete.');
  };

  private async _fetch(path: string, init: RequestInit = {}): Promise<Response> {
    if (!this.hasCompanions) throw new Error(this.configurationError ?? 'Configure a companion base URL to use saved chats, documents and skills.');
    return this._fetchUrl(`${this._root}${path}`, init);
  }

  private async _fetchUrl(url: string, init: RequestInit = {}): Promise<Response> {
    if (this.configurationError) throw new Error(this.configurationError);
    const provider: AadTokenProvider = await this._context.aadTokenProviderFactory.getTokenProvider();
    const token = await provider.getToken(this._apiResource);
    return fetch(url, { ...init, headers: { ...init.headers, Authorization: `Bearer ${token}` } });
  }

  private async _json<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await this._fetch(path, init);
    if (!response.ok) throw await readProblem(response, `The API returned an error (${response.status}).`);
    return await response.json() as T;
  }

  private async _send(method: string, path: string): Promise<void> {
    const response = await this._fetch(path, { method });
    if (!response.ok && response.status !== 404) {
      throw await readProblem(response, `The API returned an error (${response.status}).`);
    }
  }
}
