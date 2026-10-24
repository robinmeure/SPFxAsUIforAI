import * as React from 'react';
import ReactMarkdown from 'react-markdown';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  FluentProvider,
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  OverlayDrawer,
  Spinner,
  Theme,
  Tooltip,
  mergeClasses,
  useId,
  useRestoreFocusSource,
  useRestoreFocusTarget
} from '@fluentui/react-components';
import {
  AddCircle20Filled,
  ArrowClockwise16Regular,
  ArrowUp20Filled,
  Checkmark16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  Compose20Regular,
  Copy16Regular,
  Delete16Regular,
  PanelLeftContract20Regular,
  PanelLeftExpand20Regular,
  Sparkle16Filled,
  Stop20Filled
} from '@fluentui/react-icons';
import { AadTokenProvider } from '@microsoft/sp-http-base';
import { WebPartContext } from '@microsoft/sp-webpart-base';
import { useStyles } from './HelloCopilot.styles';

interface Props {
  context: WebPartContext;
  apiUrl: string;
  apiScope: string;
  apiResource: string;
  theme: Theme;
  isTeams: boolean;
}

type Status = 'streaming' | 'done' | 'stopped' | 'error';
// `text` is answer content; `activity` is a short user-facing progress step; `error` ends the reply.
interface StreamEvent { text?: string; activity?: string; error?: string; }
interface ChatMessage { id: string; role: 'user' | 'assistant'; text: string; status: Status; error?: string; activity?: string[]; }
interface Conversation { id: string; title: string; messages: ChatMessage[]; updatedAt: number; }

const MAX_CONVERSATIONS = 50;
const COMPACT_WIDTH = 900;
const SLOW_REPLY_MS = 8000;

const newId = (): string => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

const streamReply = async (
  apiUrl: string, token: string, message: string, signal: AbortSignal, onEvent: (event: StreamEvent) => void
): Promise<void> => {
  const streamResponse = await fetch(apiUrl, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', Accept: 'text/event-stream' },
    body: JSON.stringify({ message }),
    signal
  });
  if (!streamResponse.ok) throw new Error(`The API returned an error (${streamResponse.status}). Check the API URL in the web part settings and try again.`);
  if (!streamResponse.body) throw new Error('This browser doesn\'t support streaming responses.');
  const reader = streamResponse.body.getReader();
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

const isAbort = (e: unknown): boolean => e instanceof DOMException && e.name === 'AbortError';

const loadConversations = (key: string): Conversation[] => {
  try {
    const stored = JSON.parse(window.localStorage.getItem(key) || '[]') as Conversation[];
    // A reply that was still streaming when the page closed can never finish.
    return Array.isArray(stored) ? stored.map(chat => ({
      ...chat,
      messages: chat.messages.map(m => (m.status === 'streaming' ? { ...m, status: 'stopped' as Status } : m))
    })) : [];
  } catch {
    return [];
  }
};

const saveConversations = (key: string, conversations: Conversation[]): void => {
  try { window.localStorage.setItem(key, JSON.stringify(conversations.slice(0, MAX_CONVERSATIONS))); }
  catch { /* Storage can be full or blocked; history is a convenience, not a requirement. */ }
};

const useElementWidth = (ref: React.RefObject<HTMLElement>): number => {
  const [width, setWidth] = React.useState(0);
  React.useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return undefined;
    setWidth(element.clientWidth);
    const observer = new ResizeObserver(entries => setWidth(entries[0].contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, [ref]);
  return width;
};

// In Teams the tab is the whole canvas, so the chat fills the remaining viewport height.
const useFillViewport = (ref: React.RefObject<HTMLElement>, enabled: boolean): void => {
  React.useLayoutEffect(() => {
    const element = ref.current;
    if (!enabled || !element) return undefined;
    const fit = (): void => {
      element.style.height = `${Math.max(400, window.innerHeight - element.getBoundingClientRect().top)}px`;
    };
    fit();
    window.addEventListener('resize', fit);
    return () => window.removeEventListener('resize', fit);
  }, [ref, enabled]);
};

const ActivityLog: React.FC<{ steps: string[]; streaming: boolean; hasText: boolean }> = ({ steps, streaming, hasText }) => {
  const styles = useStyles();
  const listId = useId('hello-copilot-activity');
  const [expanded, setExpanded] = React.useState<boolean>();
  // Open while the reply has no text yet so the wait is explained; collapse once the answer arrives.
  const open = expanded ?? (streaming && !hasText);
  const label = streaming ? 'Working' : `Worked through ${steps.length} step${steps.length === 1 ? '' : 's'}`;
  return (
    <div className={styles.activity}>
      <button type="button" className={styles.activityToggle} aria-expanded={open} aria-controls={listId} onClick={() => setExpanded(!open)}>
        {open ? <ChevronDown16Regular aria-hidden="true" /> : <ChevronRight16Regular aria-hidden="true" />}
        {label}
      </button>
      {open && (
        <ol id={listId} className={styles.activityList}>
          {steps.map((step, i) => (
            <li key={i} className={styles.activityStep}>
              {streaming && i === steps.length - 1
                ? <Spinner size="extra-tiny" aria-hidden="true" />
                : <Checkmark16Regular aria-hidden="true" />}
              {step}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
};

const AssistantMessage: React.FC<{ message: ChatMessage; onRetry?: () => void }> = ({ message, onRetry }) => {
  const styles = useStyles();
  const [copied, setCopied] = React.useState(false);
  const [copyError, setCopyError] = React.useState(false);
  const [slow, setSlow] = React.useState(false);
  const streaming = message.status === 'streaming';
  const steps = message.activity ?? [];

  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);

  React.useEffect(() => {
    if (!streaming || message.text) { setSlow(false); return undefined; }
    const timer = window.setTimeout(() => setSlow(true), SLOW_REPLY_MS);
    return () => window.clearTimeout(timer);
  }, [streaming, message.text]);

  const copy = async (): Promise<void> => {
    setCopied(false);
    setCopyError(false);
    try {
      await navigator.clipboard.writeText(message.text);
      setCopied(true);
    } catch {
      setCopyError(true);
    }
  };

  const phase = message.text ? 'Responding…' : slow ? 'Still working… you can stop at any time.' : (steps[steps.length - 1] ?? 'Thinking') + '…';

  return (
    <div className={styles.row}>
      <div className={styles.assistant}>
        <span className={styles.assistantName}>
          <span className={styles.avatar} aria-hidden="true"><Sparkle16Filled /></span>
          Hello Copilot
        </span>
        {steps.length > 0 && <ActivityLog steps={steps} streaming={streaming} hasText={!!message.text} />}
        {streaming && !message.text && steps.length === 0 && (
          <div className={styles.thinking}><Spinner size="extra-tiny" aria-hidden="true" />{phase}</div>
        )}
        {streaming && !message.text && steps.length > 0 && slow && <div className={styles.thinking}>{phase}</div>}
        {message.text && (
          <div className={styles.assistantText}>
            <ReactMarkdown
              skipHtml
              transformLinkUri={href => /^(https?:\/\/|mailto:|tel:)/i.test(href) ? href : ''}
              components={{
                a: ({ href, children }) => href
                  ? <Link href={href} target="_blank" rel="noopener noreferrer">{children}</Link>
                  : <span>{children}</span>,
                img: ({ alt }) => <span>{alt}</span>,
                pre: ({ children }) => <pre tabIndex={0} aria-label="Code sample">{children}</pre>,
                h1: ({ children }) => <h3>{children}</h3>,
                h2: ({ children }) => <h3>{children}</h3>
              }}
            >
              {message.text}
            </ReactMarkdown>
          </div>
        )}
        {streaming && message.text && (
          <div className={styles.thinking} aria-hidden="true"><Spinner size="extra-tiny" />Generating…</div>
        )}
        {streaming && <span role="status" className={styles.visuallyHidden}>{phase}</span>}
        {message.status === 'error' && (
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              {message.text ? 'The reply stopped early. ' : ''}{message.error}
            </MessageBarBody>
          </MessageBar>
        )}
        {!streaming && (message.text || message.status !== 'done') && (
          <div className={styles.meta}>
            {message.text && (
              <Tooltip content={copied ? 'Copied' : 'Copy response'} relationship="label" withArrow>
                <Button appearance="subtle" size="small" icon={copied ? <Checkmark16Regular /> : <Copy16Regular />} onClick={() => { void copy(); }} />
              </Tooltip>
            )}
            {onRetry && (
              <Tooltip content={message.status === 'done' ? 'Regenerate' : 'Try again'} relationship="label" withArrow>
                <Button appearance="subtle" size="small" icon={<ArrowClockwise16Regular />} onClick={onRetry} />
              </Tooltip>
            )}
            {copied && <span role="status" className={styles.visuallyHidden}>Response copied.</span>}
            {message.status === 'stopped' && <span className={styles.metaNote}>Stopped{message.text ? ' — partial response' : ''}</span>}
          </div>
        )}
        {copyError && (
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>Couldn&apos;t copy the response. Try again, or select the text and copy it manually.</MessageBarBody>
          </MessageBar>
        )}
      </div>
    </div>
  );
};

interface ComposerProps {
  inputRef: React.RefObject<HTMLTextAreaElement>;
  draft: string;
  onDraftChange: (value: string) => void;
  onSend: () => void;
  onStop: () => void;
  canStop: boolean;
  sendBlocked: boolean;
  wide: boolean;
}

const Composer: React.FC<ComposerProps> = ({ inputRef, draft, onDraftChange, onSend, onStop, canStop, sendBlocked, wide }) => {
  const styles = useStyles();
  const hintId = useId('hello-copilot-hint');

  React.useLayoutEffect(() => {
    const input = inputRef.current;
    if (!input) return undefined;
    const fit = (): void => {
      input.style.height = 'auto';
      input.style.height = `${input.scrollHeight}px`;
    };
    fit();
    // FluentProvider injects its token variables after child layout effects, so measure again once they apply.
    const frame = window.requestAnimationFrame(fit);
    let lastWidth = input.clientWidth;
    const observer = new ResizeObserver(() => {
      if (input.clientWidth !== lastWidth) { lastWidth = input.clientWidth; fit(); }
    });
    observer.observe(input);
    return () => { window.cancelAnimationFrame(frame); observer.disconnect(); };
  }, [draft, inputRef]);

  const onKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>): void => {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      onSend();
    }
  };

  return (
    <div className={mergeClasses(styles.composer, wide && styles.composerWide)}>
      <textarea
        ref={inputRef}
        className={styles.input}
        rows={1}
        value={draft}
        placeholder="Message Hello Copilot"
        aria-label="Message"
        aria-describedby={hintId}
        onChange={e => onDraftChange(e.target.value)}
        onKeyDown={onKeyDown}
      />
      <span id={hintId} className={styles.visuallyHidden}>Press Enter to send and Shift+Enter for a new line.</span>
      {canStop ? (
        <Tooltip content="Stop generating" relationship="label" withArrow>
          <Button className={styles.actionButton} appearance="secondary" shape="circular" icon={<Stop20Filled />} onClick={onStop} />
        </Tooltip>
      ) : (
        <Tooltip content="Send" relationship="label" withArrow>
          <Button className={styles.actionButton} appearance="primary" shape="circular" icon={<ArrowUp20Filled />} disabled={!draft.trim() || sendBlocked} onClick={onSend} />
        </Tooltip>
      )}
    </div>
  );
};

interface SidebarProps {
  id: string;
  conversations: Conversation[];
  activeId: string | undefined;
  generatingId: string | undefined;
  onNewChat: () => void;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
  onClose: () => void;
}

const DeleteConversation: React.FC<{ conversation: Conversation; onDelete: () => void }> = ({ conversation, onDelete }) => {
  const styles = useStyles();
  const descriptionId = useId('hello-copilot-delete');

  return (
    <Dialog>
      <DialogTrigger disableButtonEnhancement>
        <Button
          className={mergeClasses('hc-delete', styles.deleteButton)}
          appearance="subtle"
          size="small"
          icon={<Delete16Regular />}
          aria-label={`Delete conversation: ${conversation.title}`}
          title="Delete conversation"
        />
      </DialogTrigger>
      <DialogSurface aria-describedby={descriptionId}>
        <DialogBody>
          <DialogTitle>Delete conversation?</DialogTitle>
          <DialogContent id={descriptionId} className={styles.deleteDescription}>
            Delete &quot;{conversation.title}&quot; from this browser? This cannot be undone.
          </DialogContent>
          <DialogActions>
            <DialogTrigger disableButtonEnhancement action="close">
              <Button appearance="secondary">Cancel</Button>
            </DialogTrigger>
            <Button appearance="primary" onClick={onDelete}>Delete</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

const Sidebar: React.FC<SidebarProps> = ({ id, conversations, activeId, generatingId, onNewChat, onSelect, onDelete, onClose }) => {
  const styles = useStyles();
  const labelId = useId('hello-copilot-chats');
  const newChatRef = React.useRef<HTMLButtonElement>(null);
  return (
    <nav
      id={id}
      className={styles.sidebar}
      aria-label="Conversations"
    >
      <div className={styles.sidebarTop}>
        <Tooltip content="Close sidebar" relationship="label" withArrow>
          <Button appearance="subtle" icon={<PanelLeftContract20Regular />} onClick={onClose} />
        </Tooltip>
      </div>
      <div className={styles.sidebarBody}>
        <button
          ref={newChatRef}
          type="button"
          className={mergeClasses(styles.navItem, !activeId && styles.navItemSelected)}
          aria-current={!activeId ? 'page' : undefined}
          onClick={onNewChat}
        >
          <span className={styles.newChatIcon} aria-hidden="true"><AddCircle20Filled /></span>
          New chat
        </button>
        <h3 id={labelId} className={styles.sectionLabel}>Chats</h3>
        {conversations.length === 0
          ? <p className={styles.sidebarEmpty}>Your conversations appear here.</p>
          : (
            <ul className={styles.chatList} aria-labelledby={labelId}>
              {conversations.map(chat => (
                <li key={chat.id} className={styles.chatRow}>
                  <button
                    type="button"
                    className={mergeClasses(styles.navItem, chat.id === activeId && styles.navItemSelected)}
                    aria-current={chat.id === activeId ? 'page' : undefined}
                    title={chat.title}
                    onClick={() => onSelect(chat.id)}
                  >
                    <span className={styles.chatTitle}>{chat.title}</span>
                    {chat.id === generatingId && <Spinner size="extra-tiny" label={{ children: 'Generating', className: styles.visuallyHidden }} />}
                  </button>
                  <DeleteConversation conversation={chat} onDelete={() => {
                    onDelete(chat.id);
                    queueMicrotask(() => newChatRef.current?.focus());
                  }} />
                </li>
              ))}
            </ul>
          )}
      </div>
    </nav>
  );
};

const Chat: React.FC<Omit<Props, 'theme'>> = ({ context, apiUrl, apiResource, isTeams }) => {
  const styles = useStyles();
  const sidebarId = useId('hello-copilot-sidebar');
  const restoreFocusSource = useRestoreFocusSource();
  const restoreFocusTarget = useRestoreFocusTarget();
  const storageKey = `hello-copilot:conversations:${context.instanceId}`;
  const [conversations, setConversations] = React.useState<Conversation[]>(() => loadConversations(storageKey));
  const [activeId, setActiveId] = React.useState<string>();
  const [draft, setDraft] = React.useState('');
  const [streamingChatId, setStreamingChatId] = React.useState<string>();
  const [sidebarPreference, setSidebarPreference] = React.useState<boolean>();
  const controller = React.useRef<AbortController>();
  const rootRef = React.useRef<HTMLDivElement>(null);
  const threadRef = React.useRef<HTMLDivElement>(null);
  const inputRef = React.useRef<HTMLTextAreaElement>(null);
  const sidebarToggleRef = React.useRef<HTMLButtonElement>(null);
  const pinnedToBottom = React.useRef(true);

  const width = useElementWidth(rootRef);
  useFillViewport(rootRef, isTeams);
  const compact = width > 0 && width < COMPACT_WIDTH;
  const sidebarOpen = sidebarPreference ?? !compact;

  const active = conversations.find(chat => chat.id === activeId);
  const messages = active?.messages ?? [];
  const busy = streamingChatId !== undefined;

  React.useEffect(() => () => controller.current?.abort(), []);

  React.useEffect(() => {
    if (!busy) saveConversations(storageKey, conversations);
  }, [conversations, busy, storageKey]);

  React.useLayoutEffect(() => {
    const thread = threadRef.current;
    if (thread && pinnedToBottom.current) thread.scrollTop = thread.scrollHeight;
  }, [messages]);

  React.useEffect(() => { setSidebarPreference(undefined); }, [compact]);

  const onScroll = (): void => {
    const thread = threadRef.current;
    if (thread) pinnedToBottom.current = thread.scrollHeight - thread.scrollTop - thread.clientHeight < 48;
  };

  const updateMessage = (chatId: string, messageId: string, update: (message: ChatMessage) => ChatMessage): void =>
    setConversations(current => current.map(chat => (chat.id !== chatId ? chat : {
      ...chat,
      messages: chat.messages.map(message => (message.id === messageId ? update(message) : message))
    })));

  const closeOverlay = (): void => { if (compact) setSidebarPreference(false); };

  const startNewChat = (): void => {
    setActiveId(undefined);
    setDraft('');
    closeOverlay();
    inputRef.current?.focus();
  };

  const selectChat = (id: string): void => {
    setActiveId(id);
    pinnedToBottom.current = true;
    closeOverlay();
  };

  const deleteChat = (id: string): void => {
    if (id === streamingChatId) controller.current?.abort();
    setConversations(current => current.filter(chat => chat.id !== id));
    if (id === activeId) setActiveId(undefined);
  };

  const send = async (override?: string): Promise<void> => {
    const text = (override ?? draft).trim();
    if (!text || busy) return;
    const abortController = new AbortController();
    controller.current = abortController;
    const chatId = activeId ?? newId();
    const replyId = newId();
    const turn: ChatMessage[] = [
      { id: newId(), role: 'user', text, status: 'done' },
      { id: replyId, role: 'assistant', text: '', status: 'streaming' }
    ];
    pinnedToBottom.current = true;
    setConversations(current => {
      const existing = current.find(chat => chat.id === chatId);
      const updated: Conversation = existing
        ? { ...existing, messages: [...existing.messages, ...turn], updatedAt: Date.now() }
        : { id: chatId, title: text.replace(/\s+/g, ' ').slice(0, 80), messages: turn, updatedAt: Date.now() };
      return [updated, ...current.filter(chat => chat.id !== chatId)];
    });
    setActiveId(chatId);
    if (override === undefined) setDraft('');
    setStreamingChatId(chatId);
    window.requestAnimationFrame(() => inputRef.current?.focus());
    try {
      const provider: AadTokenProvider = await context.aadTokenProviderFactory.getTokenProvider();
      const token = await provider.getToken(apiResource);
      await streamReply(apiUrl, token, text, abortController.signal, event => updateMessage(chatId, replyId, message => ({
        ...message,
        text: message.text + (event.text ?? ''),
        activity: event.activity ? [...(message.activity ?? []), event.activity] : message.activity
      })));
      updateMessage(chatId, replyId, message => ({ ...message, status: 'done' }));
    } catch (e) {
      updateMessage(chatId, replyId, message => isAbort(e)
        ? { ...message, status: 'stopped' }
        : { ...message, status: 'error', error: e instanceof Error ? e.message : 'Couldn\'t reach the API. Check your connection and try again.' });
    } finally {
      if (controller.current === abortController) {
        controller.current = undefined;
        setStreamingChatId(undefined);
      }
    }
  };

  const retryLast = (): void => {
    const last = messages[messages.length - 1];
    const prompt = messages[messages.length - 2];
    if (!active || busy || last?.role !== 'assistant' || prompt?.role !== 'user') return;
    const chatId = active.id;
    setConversations(current => current.map(chat => (chat.id === chatId ? { ...chat, messages: chat.messages.slice(0, -2) } : chat)));
    void send(prompt.text);
  };

  const generatingChat = busy && streamingChatId !== activeId ? conversations.find(chat => chat.id === streamingChatId) : undefined;
  const notice = generatingChat && (
    <div className={styles.notice}>
      <MessageBar intent="info" layout="multiline">
        <MessageBarBody>A response is still generating in &quot;{generatingChat.title}&quot;.</MessageBarBody>
        <MessageBarActions>
          <Button size="small" onClick={() => selectChat(generatingChat.id)}>Go to chat</Button>
          <Button size="small" onClick={() => controller.current?.abort()}>Stop</Button>
        </MessageBarActions>
      </MessageBar>
    </div>
  );

  const composer = (
    <Composer
      inputRef={inputRef}
      draft={draft}
      onDraftChange={setDraft}
      onSend={() => { void send(); }}
      onStop={() => controller.current?.abort()}
      canStop={busy && streamingChatId === activeId}
      sendBlocked={busy}
      wide={messages.length > 0}
    />
  );

  const sidebar = (
    <Sidebar
      id={sidebarId}
      conversations={conversations}
      activeId={activeId}
      generatingId={streamingChatId}
      onNewChat={startNewChat}
      onSelect={selectChat}
      onDelete={deleteChat}
      onClose={() => setSidebarPreference(false)}
    />
  );

  return (
    <div ref={rootRef} className={mergeClasses(styles.root, isTeams ? styles.rootTeams : styles.rootPage)}>
      {compact ? (
        <OverlayDrawer
          {...restoreFocusSource}
          className={styles.sidebarOverlay}
          mountNode={rootRef.current}
          open={sidebarOpen}
          position="start"
          aria-label="Chat history"
          onOpenChange={(_, data) => setSidebarPreference(data.open)}
          surfaceMotion={{
            onMotionFinish: (_, data) => {
              // Return focus after Fluent unmounts the drawer, including pointer dismissals.
              if (data.direction === 'exit') queueMicrotask(() => sidebarToggleRef.current?.focus());
            }
          }}
        >
          {sidebar}
        </OverlayDrawer>
      ) : sidebarOpen && sidebar}

      <section className={styles.main} aria-label="Hello Copilot">
        <div className={styles.topBar}>
          {(compact || !sidebarOpen) && (
            <Tooltip content="Open sidebar" relationship="label" withArrow>
              <Button
                {...restoreFocusTarget}
                ref={sidebarToggleRef}
                appearance="subtle"
                icon={<PanelLeftExpand20Regular />}
                aria-expanded={sidebarOpen}
                aria-controls={sidebarId}
                onClick={() => setSidebarPreference(true)}
              />
            </Tooltip>
          )}
          {active ? <h2 className={styles.topTitle}>{active.title}</h2> : <span className={styles.topTitle} />}
          {active && (
            <Tooltip content="New chat" relationship="label" withArrow>
              <Button appearance="subtle" icon={<Compose20Regular />} onClick={startNewChat} />
            </Tooltip>
          )}
        </div>

        {messages.length === 0 ? (
          <div className={styles.welcome}>
            <h2 className={styles.greeting}>Hi, what can I help you with?</h2>
            {notice}
            {composer}
          </div>
        ) : (
          <>
            <div
              ref={threadRef}
              className={styles.thread}
              role="log"
              aria-live="polite"
              aria-label="Conversation"
              tabIndex={0}
              onScroll={onScroll}
            >
              <div className={styles.threadInner}>
                {messages.map(message => message.role === 'user'
                  ? (
                    <div key={message.id} className={mergeClasses(styles.row, styles.rowUser)}>
                      <div className={styles.userBubble}>{message.text}</div>
                    </div>
                  )
                  : <AssistantMessage
                    key={message.id}
                    message={message}
                    onRetry={!busy && message === messages[messages.length - 1] && messages.length >= 2 ? retryLast : undefined}
                  />)}
              </div>
            </div>
            <div className={styles.composerDock}>{notice}{composer}</div>
          </>
        )}
      </section>
    </div>
  );
};

export const HelloCopilot: React.FC<Props> = ({ theme, ...props }) => {
  const styles = useStyles();
  return (
    <FluentProvider theme={theme} className={styles.provider}>
      <Chat {...props} />
    </FluentProvider>
  );
};
