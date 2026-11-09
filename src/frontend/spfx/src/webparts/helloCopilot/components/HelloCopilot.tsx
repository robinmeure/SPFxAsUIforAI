import * as React from 'react';
import ReactMarkdown from 'react-markdown';
import {
  Avatar,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Divider,
  FluentProvider,
  InteractionTag,
  InteractionTagPrimary,
  InteractionTagSecondary,
  Link,
  Menu,
  MenuGroup,
  MenuGroupHeader,
  MenuItem,
  MenuItemRadio,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  OverlayDrawer,
  Spinner,
  TagGroup,
  Theme,
  Tooltip,
  mergeClasses,
  useArrowNavigationGroup,
  useId,
  useRestoreFocusSource,
  useRestoreFocusTarget
} from '@fluentui/react-components';
import {
  AddCircle20Filled,
  AddCircle20Regular,
  ArrowClockwise16Regular,
  ArrowUp20Filled,
  ArrowUpload20Regular,
  Attach20Regular,
  Checkmark16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  Compose20Regular,
  Copy16Regular,
  Delete16Regular,
  Globe20Regular,
  Library20Filled,
  Library20Regular,
  PanelLeftContract20Regular,
  PanelLeftExpand20Regular,
  Sparkle16Filled,
  Stop20Filled,
  Wand16Regular,
  Wand20Regular
} from '@fluentui/react-icons';
import { WebPartContext } from '@microsoft/sp-webpart-base';
import {
  ApiError, ChatApi, ChatSummary, PassageCitation, RetrievalDetails, SUPPORTED_EXTENSIONS, SkillSummary, UserDocument, conflictOf, skillLabel
} from './api';
import { CitationEvidence } from './Citations';
import { citationLinks, citationLocation } from './citationHelpers';
import { DocumentSetPill, DraftAttachments, SetConflict, sameSet } from './DocumentSet';
import { PickedFile, SharePointFilePicker } from './FilePicker';
import { useStyles } from './HelloCopilot.styles';
import { Library, LibraryTarget } from './Library';
import { DocumentErrors, useDocuments } from './useDocuments';
import { ChatListState } from './chatListState';

interface Props {
  context: WebPartContext;
  apiUrl: string;
  apiScope: string;
  apiResource: string;
  companionBaseUrl?: string;
  theme: Theme;
  isTeams: boolean;
}

type Status = 'streaming' | 'done' | 'stopped' | 'error';
interface ChatMessage {
  id: string; role: 'user' | 'assistant' | 'event'; text: string; status: Status; error?: string; activity?: string[];
  citations?: PassageCitation[]; retrieval?: RetrievalDetails; createdAt?: string; skill?: string; followUpQuestions?: string[];
}
// `messages` is undefined until the chat is opened and its history is loaded from the API.
interface Conversation {
  id: string; title: string; messages?: ChatMessage[]; updatedAt: number; loadError?: string;
  documentIds: string[]; documents?: UserDocument[];
}

const COMPACT_WIDTH = 900;
const SIDEBAR_WIDTH = 248;
const SLOW_REPLY_MS = 8000;
// Chats get their server id from the first `chatId` stream event; until then they use a local id.
const LOCAL_PREFIX = 'local-';

const newId = (): string => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

const isAbort = (e: unknown): boolean => e instanceof DOMException && e.name === 'AbortError';

const formatEventTime = (iso: string): string => {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
};

const toConversation = (chat: ChatSummary): Conversation => ({
  id: chat.id, title: chat.title, updatedAt: Date.parse(chat.updatedAt), documentIds: chat.documentIds ?? []
});

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

const AssistantMessage: React.FC<{
  message: ChatMessage; onRetry?: () => void; onFollowUp?: (question: string) => void; followUpDisabled?: boolean;
}> = ({ message, onRetry, onFollowUp, followUpDisabled }) => {
  const styles = useStyles();
  const [copied, setCopied] = React.useState(false);
  const [copyError, setCopyError] = React.useState(false);
  const [slow, setSlow] = React.useState(false);
  const [selectedCitation, setSelectedCitation] = React.useState<number>();
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
          <Avatar size={24} color="brand" icon={<Sparkle16Filled />} aria-hidden="true" />
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
              remarkPlugins={[citationLinks]}
              transformLinkUri={href => /^(https?:\/\/|mailto:|tel:|#citation-\d+$)/i.test(href) ? href : ''}
              components={{
                a: ({ href, children }) => {
                  const match = /^#citation-(\d+)$/.exec(href ?? '');
                  if (match) {
                    const citation = message.citations?.find(source => source.number === Number(match[1]));
                    return citation ? <button type="button" className={styles.inlineCitation}
                      title={`${citation.title} · ${citationLocation(citation)}`}
                      aria-label={`Show citation ${citation.number}: ${citation.title}`}
                      aria-expanded={selectedCitation === citation.number}
                      onClick={() => setSelectedCitation(citation.number)}>{children}</button>
                      : <span title="No supporting passage was provided for this reference">{children} (unverified)</span>;
                  }
                  return href ? <Link href={href} target="_blank" rel="noopener noreferrer">{children}</Link> : <span>{children}</span>;
                },
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
        <CitationEvidence citations={message.citations ?? []} retrieval={message.retrieval}
          selected={selectedCitation} onSelect={setSelectedCitation} />
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
        {message.status === 'done' && onFollowUp && !!message.followUpQuestions?.length && (
          <div className={styles.followUps} role="group" aria-label="Suggested follow-up questions">
            {message.followUpQuestions.map(question => (
              <Button key={question} className={styles.followUp} appearance="outline" size="small"
                disabled={followUpDisabled} onClick={() => onFollowUp(question)}>{question}</Button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

interface ComposerProps {
  attachmentsEnabled: boolean;
  inputRef: React.RefObject<HTMLTextAreaElement>;
  draft: string;
  onDraftChange: (value: string) => void;
  onSend: () => void;
  onStop: () => void;
  canStop: boolean;
  sendBlocked: boolean;
  wide: boolean;
  onUpload: () => void;
  onPickSharePoint: () => void;
  placeholder: string;
  attachments?: React.ReactNode;
  skills: SkillSummary[];
  selectedSkill?: SkillSummary;
  onSelectSkill: (skill: SkillSummary | undefined) => void;
}

const Composer: React.FC<ComposerProps> = ({
  inputRef, draft, onDraftChange, onSend, onStop, canStop, sendBlocked, wide, onUpload, onPickSharePoint, placeholder, attachments,
  skills, selectedSkill, onSelectSkill, attachmentsEnabled
}) => {
  const styles = useStyles();
  const hintId = useId('hello-copilot-hint');
  const [skillsOpen, setSkillsOpen] = React.useState(false);

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

  // "/" in an empty message box opens the skills menu, like slash commands in Teams.
  const onChange = (value: string): void => {
    if (value === '/' && !draft && skills.length > 0) {
      setSkillsOpen(true);
      return;
    }
    onDraftChange(value);
  };

  const chooseSkill = (skill: SkillSummary | undefined): void => {
    onSelectSkill(skill);
    setSkillsOpen(false);
    window.requestAnimationFrame(() => inputRef.current?.focus());
  };

  const selectedLabel = selectedSkill && skillLabel(selectedSkill.name);

  return (
    <div className={mergeClasses(styles.composer, wide && styles.composerWide)}>
      {(selectedSkill || attachments) && (
        <div className={styles.composerTop}>
          {selectedSkill && (
            <TagGroup size="small" aria-label="Selected skill" onDismiss={() => chooseSkill(undefined)}>
              <InteractionTag value={selectedSkill.name} appearance="brand" size="small" title={selectedSkill.description}>
                <InteractionTagPrimary hasSecondaryAction icon={<Wand16Regular />}>
                  <span className={styles.tagContent}>{selectedLabel}</span>
                </InteractionTagPrimary>
                <InteractionTagSecondary aria-label={`Remove skill ${selectedLabel}`} />
              </InteractionTag>
            </TagGroup>
          )}
          {attachments}
        </div>
      )}
      {attachmentsEnabled && <Menu positioning="above-start">
        <Tooltip content="Add a document" relationship="label" withArrow>
          <MenuTrigger disableButtonEnhancement>
            <Button className={styles.attachButton} appearance="subtle" shape="circular" icon={<Attach20Regular />} />
          </MenuTrigger>
        </Tooltip>
        <MenuPopover>
          <MenuList>
            <MenuItem icon={<ArrowUpload20Regular />} onClick={onUpload}>Upload from this device</MenuItem>
            <MenuItem icon={<Globe20Regular />} onClick={onPickSharePoint}>Choose from SharePoint</MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>}
      {skills.length > 0 && (
        <Menu
          open={skillsOpen}
          onOpenChange={(_, data) => setSkillsOpen(data.open)}
          positioning="above-start"
          checkedValues={{ skill: selectedSkill ? [selectedSkill.name] : [] }}
        >
          <Tooltip content="Use a skill (or type /)" relationship="label" withArrow>
            <MenuTrigger disableButtonEnhancement>
              <Button className={styles.attachButton} appearance="subtle" shape="circular" icon={<Wand20Regular />}
                aria-pressed={!!selectedSkill} />
            </MenuTrigger>
          </Tooltip>
          <MenuPopover>
            <MenuList className={styles.skillMenu}>
              <MenuGroup>
                <MenuGroupHeader>Skills</MenuGroupHeader>
                {skills.map(skill => (
                  <MenuItemRadio key={skill.name} name="skill" value={skill.name}
                    subText={{ children: skill.description, className: styles.skillDescription }}
                    onClick={() => chooseSkill(selectedSkill?.name === skill.name ? undefined : skill)}>
                    {skillLabel(skill.name)}
                  </MenuItemRadio>
                ))}
              </MenuGroup>
            </MenuList>
          </MenuPopover>
        </Menu>
      )}
      <textarea
        ref={inputRef}
        className={styles.input}
        rows={1}
        value={draft}
        placeholder={selectedLabel ? `Describe your request for ${selectedLabel}` : placeholder}
        aria-label="Message"
        aria-describedby={hintId}
        onChange={e => onChange(e.target.value)}
        onKeyDown={onKeyDown}
      />
      <span id={hintId} className={styles.visuallyHidden}>
        Press Enter to send and Shift+Enter for a new line.{skills.length > 0 ? ' Type / to choose a skill.' : ''}
      </span>
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
  libraryEnabled: boolean;
  id: string;
  conversations: Conversation[];
  loading: boolean;
  loadError: string | undefined;
  onReload: () => void;
  activeId: string | undefined;
  generatingId: string | undefined;
  onNewChat: () => void;
  libraryActive: boolean;
  onOpenLibrary: () => void;
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
            Delete &quot;{conversation.title}&quot;? This cannot be undone.
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

const Sidebar: React.FC<SidebarProps> = ({
  id, conversations, loading, loadError, onReload, activeId, generatingId, onNewChat, libraryActive, onOpenLibrary, onSelect, onDelete, onClose, libraryEnabled
}) => {
  const styles = useStyles();
  const labelId = useId('hello-copilot-chats');
  const navArrowProps = useArrowNavigationGroup({ axis: 'vertical' });
  const listArrowProps = useArrowNavigationGroup({ axis: 'vertical' });
  const newChatRef = React.useRef<HTMLButtonElement>(null);
  const newChatActive = !activeId && !libraryActive;
  const selectedId = libraryActive ? undefined : activeId;
  return (
    <nav
      id={id}
      className={styles.sidebar}
      aria-label="Hello Copilot"
    >
      <div className={styles.sidebarTop}>
        <Tooltip content="Close sidebar" relationship="label" withArrow>
          <Button appearance="subtle" icon={<PanelLeftContract20Regular />} onClick={onClose} />
        </Tooltip>
      </div>
      <div className={styles.sidebarBody}>
        <div {...navArrowProps} className={styles.chatList}>
          <button
            ref={newChatRef}
            type="button"
            className={mergeClasses(styles.navItem, newChatActive && styles.navItemSelected)}
            aria-current={newChatActive ? 'page' : undefined}
            onClick={onNewChat}
          >
            <span className={styles.newChatIcon} aria-hidden="true">
              {newChatActive ? <AddCircle20Filled /> : <AddCircle20Regular />}
            </span>
            New chat
          </button>
          {libraryEnabled && <button
            type="button"
            className={mergeClasses(styles.navItem, libraryActive && styles.navItemSelected)}
            aria-current={libraryActive ? 'page' : undefined}
            onClick={onOpenLibrary}
          >
            <span className={styles.navIcon} aria-hidden="true">{libraryActive ? <Library20Filled /> : <Library20Regular />}</span>
            Library
          </button>}
        </div>
        <h3 id={labelId} className={styles.sectionLabel}>Recent</h3>
        {loadError && (
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>{loadError}</MessageBarBody>
            <MessageBarActions><Button size="small" onClick={onReload}>Retry</Button></MessageBarActions>
          </MessageBar>
        )}
        {loading && conversations.length === 0
          ? <Spinner size="tiny" label="Loading chats…" />
          : conversations.length === 0
          ? (!loadError && <p className={styles.sidebarEmpty}>Your conversations appear here.</p>)
          : (
            <ul {...listArrowProps} className={styles.chatList} aria-labelledby={labelId}>
              {conversations.map(chat => (
                <li key={chat.id} className={styles.chatRow}>
                  <button
                    type="button"
                    className={mergeClasses(styles.navItem, chat.id === selectedId && styles.navItemSelected)}
                    aria-current={chat.id === selectedId ? 'page' : undefined}
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

const Chat: React.FC<Omit<Props, 'theme'>> = ({ context, apiUrl, apiResource, companionBaseUrl, isTeams }) => {
  const styles = useStyles();
  const sidebarId = useId('hello-copilot-sidebar');
  const restoreFocusSource = useRestoreFocusSource();
  const restoreFocusTarget = useRestoreFocusTarget();
  const api = React.useMemo(() => new ChatApi(context, apiUrl, apiResource, companionBaseUrl), [context, apiUrl, apiResource, companionBaseUrl]);
  const listState = React.useMemo(() => new ChatListState(), [api]);
  const documents = useDocuments(api);
  const [conversations, setConversations] = React.useState<Conversation[]>([]);
  const [chatsLoading, setChatsLoading] = React.useState(true);
  const [chatsError, setChatsError] = React.useState<string>();
  const [chatError, setChatError] = React.useState<string>();
  const [loadingChatId, setLoadingChatId] = React.useState<string>();
  const [view, setView] = React.useState<'chats' | 'library'>('chats');
  const [libraryTarget, setLibraryTarget] = React.useState<LibraryTarget>();
  // Files chosen with the composer's paperclip join the chat; files added in the Library only join the library.
  const uploadDestination = React.useRef<'chat' | 'library'>('chat');
  const [pickerOpen, setPickerOpen] = React.useState(false);
  const [attachmentsSaving, setAttachmentsSaving] = React.useState(false);
  const [attachmentsError, setAttachmentsError] = React.useState<string>();
  const [setConflict, setSetConflict] = React.useState<SetConflict>();
  const [draftDocumentIds, setDraftDocumentIds] = React.useState<string[]>([]);
  const [skills, setSkills] = React.useState<SkillSummary[]>([]);
  const [selectedSkill, setSelectedSkill] = React.useState<SkillSummary>();
  const attachmentLock = React.useRef(false);
  const [activeId, setActiveId] = React.useState<string>();
  const [draft, setDraft] = React.useState('');
  const [streamingChatId, setStreamingChatId] = React.useState<string>();
  const [sidebarPreference, setSidebarPreference] = React.useState<boolean>();
  const controller = React.useRef<AbortController>();
  const rootRef = React.useRef<HTMLDivElement>(null);
  const threadRef = React.useRef<HTMLDivElement>(null);
  const inputRef = React.useRef<HTMLTextAreaElement>(null);
  const sidebarToggleRef = React.useRef<HTMLButtonElement>(null);
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  const pinnedToBottom = React.useRef(true);

  const width = useElementWidth(rootRef);
  useFillViewport(rootRef, isTeams);
  const compact = width > 0 && width < COMPACT_WIDTH;
  const sidebarOpen = sidebarPreference ?? !compact;

  const active = conversations.find(chat => chat.id === activeId);
  const messages = active?.messages ?? [];
  const busy = streamingChatId !== undefined;
  const activeLoading = activeId !== undefined && activeId === loadingChatId;
  const selectedDocumentIds = active ? active.documentIds : draftDocumentIds;

  // One conversation per non-empty document set; an empty set is a general chat.
  const findConflict = (ids: readonly string[], exceptId: string | undefined): SetConflict | undefined => {
    if (!ids.length) return undefined;
    const owner = conversations.find(chat => chat.id !== exceptId && chat.id.indexOf(LOCAL_PREFIX) !== 0 && sameSet(chat.documentIds, ids));
    return owner && { chatId: owner.id, title: owner.title };
  };
  const draftConflict = !active ? findConflict(draftDocumentIds, undefined) : undefined;

  React.useEffect(() => () => { controller.current?.abort(); listState.invalidate(); }, [listState]);

  const loadChats = React.useCallback(async (): Promise<void> => {
    if (!api.hasCompanions) { setChatsLoading(false); return; }
    const snapshot = listState.begin();
    setChatsLoading(true);
    setChatsError(undefined);
    try {
      const chats = await api.listChats();
      setConversations(current => listState.merge(snapshot, current, chats.map(toConversation),
        (local, remote) => ({ ...local, title: remote.title, updatedAt: remote.updatedAt, documentIds: remote.documentIds })));
    } catch (e) {
      if (listState.current(snapshot)) setChatsError(e instanceof Error ? `Couldn't load your chats. ${e.message}` : 'Couldn\'t load your chats.');
    } finally {
      if (listState.current(snapshot)) setChatsLoading(false);
    }
  }, [api, listState]);

  React.useEffect(() => { void loadChats(); }, [loadChats]);

  // Without skills (or if they can't be loaded) the composer simply has no skills button.
  React.useEffect(() => {
    if (!api.hasCompanions) return;
    api.listSkills().then(setSkills).catch(() => setSkills([]));
  }, [api]);

  React.useLayoutEffect(() => {
    const thread = threadRef.current;
    if (thread && pinnedToBottom.current) thread.scrollTop = thread.scrollHeight;
  }, [messages]);

  React.useEffect(() => { setSidebarPreference(undefined); }, [compact]);

  const onScroll = (): void => {
    const thread = threadRef.current;
    if (thread) pinnedToBottom.current = thread.scrollHeight - thread.scrollTop - thread.clientHeight < 48;
  };

  const updateMessage = (chatId: string, messageId: string, update: (message: ChatMessage) => ChatMessage): void => {
    listState.touch(chatId);
    setConversations(current => current.map(chat => (chat.id !== chatId ? chat : {
      ...chat,
      messages: (chat.messages ?? []).map(message => (message.id === messageId ? update(message) : message))
    })));
  };

  const closeOverlay = (): void => { if (compact) setSidebarPreference(false); };

  const startNewChat = (): void => {
    setView('chats');
    setLibraryTarget(undefined);
    setActiveId(undefined);
    setDraft('');
    setChatError(undefined);
    setAttachmentsError(undefined);
    setSetConflict(undefined);
    setDraftDocumentIds([]);
    closeOverlay();
    inputRef.current?.focus();
  };

  const loadChat = async (id: string): Promise<void> => {
    setLoadingChatId(id);
    setConversations(current => current.map(chat => (chat.id === id ? { ...chat, loadError: undefined } : chat)));
    try {
      const details = await api.getChat(id);
      listState.touch(id);
      const history: ChatMessage[] = details.messages.map(m => ({
        id: m.id, role: m.role, text: m.content, status: 'done', citations: m.citations, retrieval: m.retrieval, createdAt: m.createdAt,
        followUpQuestions: m.followUpQuestions,
        skill: m.skill ?? undefined
      }));
      setConversations(current => current.map(chat => (chat.id === id ? {
        ...chat, title: details.title, messages: history, documentIds: details.documentIds ?? [], documents: details.documents
      } : chat)));
    } catch (e) {
      const loadError = e instanceof ApiError && e.status === 404
        ? 'This conversation no longer exists.'
        : `Couldn't load this conversation. ${e instanceof Error ? e.message : ''}`.trim();
      setConversations(current => current.map(chat => (chat.id === id ? { ...chat, loadError } : chat)));
    } finally {
      setLoadingChatId(current => (current === id ? undefined : current));
    }
  };

  const selectChat = (id: string): void => {
    setView('chats');
    setLibraryTarget(undefined);
    setActiveId(id);
    setChatError(undefined);
    setAttachmentsError(undefined);
    setSetConflict(undefined);
    pinnedToBottom.current = true;
    closeOverlay();
    const chat = conversations.find(c => c.id === id);
    if (chat && !chat.messages && id !== loadingChatId) void loadChat(id);
  };

  const deleteChat = async (id: string): Promise<void> => {
    if (id === streamingChatId) controller.current?.abort();
    setChatError(undefined);
    if (api.hasCompanions && id.indexOf(LOCAL_PREFIX) !== 0) {
      try {
        await api.deleteChat(id);
      } catch (e) {
        setChatError(`Couldn't delete the conversation. ${e instanceof Error ? e.message : ''}`.trim());
        return;
      }
    }
    listState.remove(id);
    setConversations(current => current.filter(chat => chat.id !== id));
    setActiveId(current => (current === id ? undefined : current));
  };

  /** Sends the draft, or with <paramref name="override"/> resends an earlier prompt with the skill it used. */
  const send = async (override?: string, overrideSkill?: string): Promise<void> => {
    const text = (override ?? draft).trim();
    if (!text || busy || activeLoading || attachmentLock.current || draftConflict || api.configurationError) return;
    const skill = override !== undefined ? overrideSkill : selectedSkill?.name;
    const composerSkill = selectedSkill;
    const abortController = new AbortController();
    controller.current = abortController;
    // The id changes once the server reports which chat the turn was stored in.
    let chatId = activeId ?? `${LOCAL_PREFIX}${newId()}`;
    const serverChatId = chatId.indexOf(LOCAL_PREFIX) === 0 ? undefined : chatId;
    const initialDocumentIds = serverChatId ? undefined : selectedDocumentIds;
    const replyId = newId();
    const turn: ChatMessage[] = [
      { id: newId(), role: 'user', text, status: 'done', skill },
      { id: replyId, role: 'assistant', text: '', status: 'streaming' }
    ];
    pinnedToBottom.current = true;
    setChatError(undefined);
    listState.touch(chatId);
    setConversations(current => {
      const existing = current.find(chat => chat.id === chatId);
      const updated: Conversation = existing
        ? { ...existing, title: serverChatId ? existing.title : text.replace(/\s+/g, ' ').slice(0, 80),
          messages: [...(existing.messages ?? []), ...turn], updatedAt: Date.now() }
        : { id: chatId, title: text.replace(/\s+/g, ' ').slice(0, 80), messages: turn, updatedAt: Date.now(), documentIds: initialDocumentIds ?? [] };
      return [updated, ...current.filter(chat => chat.id !== chatId)];
    });
    setActiveId(chatId);
    setDraftDocumentIds([]);
    if (override === undefined) { setDraft(''); setSelectedSkill(undefined); }
    setStreamingChatId(chatId);
    window.requestAnimationFrame(() => inputRef.current?.focus());
    try {
      await api.streamReply(text, serverChatId, abortController.signal, event => {
        if (api.hasCompanions && event.chatId && event.chatId !== chatId) {
          const from = chatId;
          const to = event.chatId;
          chatId = to;
          listState.promote(from, to);
          setConversations(current => current.map(chat => (chat.id === from ? { ...chat, id: to } : chat)));
          setActiveId(current => (current === from ? to : current));
          setStreamingChatId(current => (current === from ? to : current));
        }
        if (event.text || event.activity || event.citations || event.retrieval || event.followUpQuestions) {
          updateMessage(chatId, replyId, message => ({
            ...message,
            text: message.text + (event.text ?? ''),
            activity: event.activity ? [...(message.activity ?? []), event.activity] : message.activity,
            citations: event.citations ?? message.citations,
            retrieval: event.retrieval ?? message.retrieval,
            followUpQuestions: event.followUpQuestions ?? message.followUpQuestions
          }));
        }
      }, initialDocumentIds, skill);
      updateMessage(chatId, replyId, message => ({ ...message, status: 'done' }));
    } catch (e) {
      const conflict = conflictOf(e);
      if (conflict) {
        // The server rejected a set another conversation already owns: drop the unsent local chat and point to the owner.
        const localId = chatId;
        listState.remove(localId);
        setConversations(current => current.filter(chat => chat.id !== localId));
        setActiveId(undefined);
        setDraftDocumentIds(initialDocumentIds ?? []);
        if (override === undefined) { setDraft(text); setSelectedSkill(composerSkill); }
        setSetConflict(conflict);
        return;
      }
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
    setConversations(current => current.map(chat => (chat.id === chatId ? { ...chat, messages: (chat.messages ?? []).slice(0, -2) } : chat)));
    void send(prompt.text, prompt.skill);
  };

  const getToken = React.useCallback(async (resource: string): Promise<string> => {
    const provider = await context.aadTokenProviderFactory.getTokenProvider();
    return provider.getToken(resource);
  }, [context]);

  /** Sets the document set of a server chat, or of the unsent new chat when <paramref name="chatId"/> is undefined. */
  const applySelection = async (chatId: string | undefined, ids: string[]): Promise<void> => {
    if (ids.length > 20) throw new Error('A conversation can use at most 20 documents. The files remain in your Library.');
    const conflict = findConflict(ids, chatId);
    if (!chatId) {
      setDraftDocumentIds(ids);
      setSetConflict(conflict);
      return;
    }
    if (conflict) { setSetConflict(conflict); return; }
    try {
      const result = await api.setChatDocuments(chatId, ids);
      listState.touch(chatId);
      setSetConflict(undefined);
      setConversations(current => current.map(chat => chat.id !== chatId ? chat : {
        ...chat,
        documentIds: result.documentIds,
        messages: result.event && chat.messages
          ? [...chat.messages, { id: result.event.id, role: 'event', text: result.event.content, status: 'done', createdAt: result.event.createdAt }]
          : chat.messages
      }));
    } catch (e) {
      const serverConflict = conflictOf(e);
      if (serverConflict) { setSetConflict(serverConflict); return; }
      throw e;
    }
  };

  const withSelectionLock = async (work: () => Promise<void>, failure: string): Promise<void> => {
    if (busy || attachmentLock.current) return;
    attachmentLock.current = true;
    setAttachmentsSaving(true);
    setAttachmentsError(undefined);
    try { await work(); }
    catch (e) { setAttachmentsError(e instanceof Error ? e.message : failure); }
    finally { attachmentLock.current = false; setAttachmentsSaving(false); }
  };

  const changeSelection = (ids: string[]): void => {
    if (activeLoading) return;
    void withSelectionLock(() => applySelection(active?.id, ids), 'Couldn\'t update the conversation\'s documents.');
  };

  const attachUploaded = (upload: () => Promise<UserDocument[]>): void => {
    if (activeLoading) return;
    const chatId = active?.id;
    const existing = [...selectedDocumentIds];
    void withSelectionLock(async () => {
      const added = await upload();
      if (added.length) await applySelection(chatId, [...existing, ...added.map(document => document.id).filter(id => existing.indexOf(id) < 0)]);
    }, 'Files were uploaded but could not be added. Attach them from the Library to retry.');
  };

  /** From the Library: start a new chat with these documents, or add them to an existing chat and open it. */
  const attachFromLibrary = async (chatId: string | undefined, ids: string[]): Promise<void> => {
    setLibraryTarget(undefined);
    setView('chats');
    setChatError(undefined);
    setAttachmentsError(undefined);
    setSetConflict(undefined);
    closeOverlay();
    if (!chatId) {
      setActiveId(undefined);
      setDraft('');
      setDraftDocumentIds(ids);
      setSetConflict(findConflict(ids, undefined));
      window.requestAnimationFrame(() => inputRef.current?.focus());
      return;
    }
    const chat = conversations.find(c => c.id === chatId);
    if (!chat) return;
    setActiveId(chatId);
    pinnedToBottom.current = true;
    const merged = [...chat.documentIds, ...ids.filter(id => chat.documentIds.indexOf(id) < 0)];
    await withSelectionLock(() => applySelection(chatId, merged), 'Couldn\'t add the documents to the chat.');
    // Load after saving so the history includes the change divider.
    if (!chat.messages) await loadChat(chatId);
  };

  const openLibrary = (target?: LibraryTarget): void => {
    setLibraryTarget(target);
    setView('library');
  };

  const canPickFiles = (): boolean => api.hasCompanions && !busy && !attachmentLock.current && !activeLoading;

  const openUpload = (destination: 'chat' | 'library'): void => {
    if (!canPickFiles()) return;
    uploadDestination.current = destination;
    fileInputRef.current?.click();
  };

  const openSharePointPicker = (destination: 'chat' | 'library'): void => {
    if (!canPickFiles()) return;
    uploadDestination.current = destination;
    setPickerOpen(true);
  };

  const addFiles = (upload: () => Promise<UserDocument[]>): void => {
    if (uploadDestination.current === 'library') void upload();
    else attachUploaded(upload);
  };

  const onFilesChosen = (e: React.ChangeEvent<HTMLInputElement>): void => {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length) addFiles(() => documents.upload(files));
  };

  const onSharePointPicked = (files: PickedFile[]): void => {
    setPickerOpen(false);
    if (files.length) addFiles(() => documents.addFromSharePoint(files));
  };

  const activeConflict = active ? setConflict : undefined;
  const generatingChat = busy && streamingChatId !== activeId ? conversations.find(chat => chat.id === streamingChatId) : undefined;
  const notice = (!api.hasCompanions || generatingChat || chatError || activeConflict || draftConflict || documents.errors.length > 0 || attachmentsError) && (
    <div className={styles.notice}>
      {!api.hasCompanions && (
        <MessageBar intent={api.configurationError ? 'error' : 'info'} layout="multiline">
          <MessageBarBody>{api.configurationError ?? 'Streaming-only mode: chats stay in this tab. Configure a companion base URL to enable saved chats, documents and skills.'}</MessageBarBody>
        </MessageBar>
      )}
      <DocumentErrors state={documents} />
      {attachmentsError && (
        <MessageBar intent="error" layout="multiline"><MessageBarBody>{attachmentsError}</MessageBarBody></MessageBar>
      )}
      {draftConflict && (
        <MessageBar intent="info">
          <MessageBarBody>You already have a chat about {draftDocumentIds.length === 1 ? 'this document' : 'these documents'}: &quot;{draftConflict.title}&quot;.</MessageBarBody>
          <MessageBarActions>
            <Button size="small" appearance="primary" onClick={() => selectChat(draftConflict.chatId)}>Open it</Button>
          </MessageBarActions>
        </MessageBar>
      )}
      {activeConflict && (
        <MessageBar intent="info" layout="multiline">
          <MessageBarBody>Not changed: &quot;{activeConflict.title}&quot; already uses exactly these documents. Each set of documents has one conversation.</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => selectChat(activeConflict.chatId)}>Open it</Button>
            <Button size="small" appearance="transparent" onClick={() => setSetConflict(undefined)}>Dismiss</Button>
          </MessageBarActions>
        </MessageBar>
      )}
      {chatError && (
        <MessageBar intent="error" layout="multiline">
          <MessageBarBody>{chatError}</MessageBarBody>
        </MessageBar>
      )}
      {generatingChat && (
        <MessageBar intent="info" layout="multiline">
          <MessageBarBody>A response is still generating in &quot;{generatingChat.title}&quot;.</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => selectChat(generatingChat.id)}>Go to chat</Button>
            <Button size="small" onClick={() => controller.current?.abort()}>Stop</Button>
          </MessageBarActions>
        </MessageBar>
      )}
    </div>
  );

  const firstDocument = documents.documents.find(document => document.id === selectedDocumentIds[0]);
  const composerPlaceholder = !selectedDocumentIds.length || !firstDocument ? 'Message Hello Copilot'
    : selectedDocumentIds.length === 1 ? `Ask about ${firstDocument.fileName}`
    : `Ask about ${firstDocument.fileName} and ${selectedDocumentIds.length - 1} more`;

  const composer = (
    <Composer
      inputRef={inputRef}
      draft={draft}
      onDraftChange={setDraft}
      onSend={() => { void send(); }}
      onStop={() => controller.current?.abort()}
      canStop={busy && streamingChatId === activeId}
      sendBlocked={busy || attachmentsSaving || activeLoading || !!active?.loadError || !!draftConflict || !!api.configurationError}
      attachmentsEnabled={api.hasCompanions}
      wide={messages.length > 0}
      onUpload={() => openUpload('chat')}
      onPickSharePoint={() => openSharePointPicker('chat')}
      placeholder={composerPlaceholder}
      skills={skills}
      selectedSkill={selectedSkill}
      onSelectSkill={setSelectedSkill}
      attachments={!active && draftDocumentIds.length > 0 && (
        <DraftAttachments
          ids={draftDocumentIds}
          documents={documents.documents}
          disabled={busy || attachmentsSaving}
          onRemove={id => changeSelection(draftDocumentIds.filter(documentId => documentId !== id))}
        />
      )}
    />
  );

  const sidebar = (
    <Sidebar
      libraryEnabled={api.hasCompanions}
      id={sidebarId}
      conversations={conversations}
      loading={chatsLoading}
      loadError={chatsError}
      onReload={() => { void loadChats(); }}
      activeId={activeId}
      generatingId={streamingChatId}
      onNewChat={startNewChat}
      libraryActive={view === 'library'}
      onOpenLibrary={() => { closeOverlay(); openLibrary(); }}
      onSelect={selectChat}
      onDelete={id => { void deleteChat(id); }}
      onClose={() => setSidebarPreference(false)}
    />
  );

  const sidebarToggle = (compact || !sidebarOpen) && (
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

      {view === 'library' ? (
        <Library
          state={documents}
          chats={conversations.filter(chat => chat.id.indexOf(LOCAL_PREFIX) !== 0)}
          target={libraryTarget}
          busy={busy || attachmentsSaving}
          width={compact || !sidebarOpen ? width : width - SIDEBAR_WIDTH}
          sidebarToggle={sidebarToggle}
          onUpload={() => openUpload('library')}
          onPickSharePoint={() => openSharePointPicker('library')}
          onAttach={(chatId, ids) => { void attachFromLibrary(chatId, ids); }}
          onCancelTarget={() => { const target = libraryTarget; setLibraryTarget(undefined); setView('chats'); if (target) setActiveId(target.chatId); }}
          onOpenChat={selectChat}
        />
      ) : (
      <section className={styles.main} aria-label="Hello Copilot">
        <div className={styles.topBar}>
          {sidebarToggle}
          {active ? <h2 className={styles.topTitle}>{active.title}</h2> : <span className={styles.topTitle} />}
          {api.hasCompanions && active && !activeLoading && !active.loadError ? (
            <DocumentSetPill
              ids={active.documentIds}
              documents={documents.documents}
              busy={busy || attachmentsSaving}
              onRemove={id => { void changeSelection(active.documentIds.filter(documentId => documentId !== id)); }}
              onAdd={() => openLibrary({ chatId: active.id, title: active.title })}
            />
          ) : null}
          {active && (
            <Tooltip content="New chat" relationship="label" withArrow>
              <Button appearance="subtle" icon={<Compose20Regular />} onClick={startNewChat} />
            </Tooltip>
          )}
        </div>

        {activeLoading || active?.loadError ? (
          <div className={styles.welcome}>
            {activeLoading ? <Spinner className={styles.composerLoading} size="small" label="Loading conversation…" /> : (
              <MessageBar intent="error" layout="multiline">
                <MessageBarBody>{active?.loadError}</MessageBarBody>
                <MessageBarActions>
                  {active && <Button size="small" onClick={() => { void loadChat(active.id); }}>Retry</Button>}
                  <Button size="small" onClick={startNewChat}>New chat</Button>
                </MessageBarActions>
              </MessageBar>
            )}
          </div>
        ) : messages.length === 0 ? (
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
                {messages.map(message => message.role === 'event'
                  ? (
                    <Divider key={message.id} appearance="subtle" className={styles.eventDivider} role="note">
                      {message.text}{message.createdAt ? ` · ${formatEventTime(message.createdAt)}` : ''}
                    </Divider>
                  )
                  : message.role === 'user'
                  ? (
                    <div key={message.id} className={mergeClasses(styles.row, styles.rowUser)}>
                      {message.skill && (
                        <span className={styles.userSkill}><Wand16Regular aria-hidden="true" />{skillLabel(message.skill)}</span>
                      )}
                      <div className={styles.userBubble}>{message.text}</div>
                    </div>
                  )
                  : <AssistantMessage
                    key={message.id}
                    message={message}
                    onRetry={!busy && message === messages[messages.length - 1] && messages.length >= 2 ? retryLast : undefined}
                    onFollowUp={message === messages[messages.length - 1] ? question => { void send(question); } : undefined}
                    followUpDisabled={busy || activeLoading || attachmentsSaving || !!draftConflict || !!api.configurationError}
                  />)}
              </div>
            </div>
            <div className={styles.composerDock}>{notice}{composer}</div>
          </>
        )}
      </section>
      )}

      <input
        ref={fileInputRef}
        type="file"
        multiple
        accept={SUPPORTED_EXTENSIONS.join(',')}
        className={styles.visuallyHidden}
        tabIndex={-1}
        aria-hidden="true"
        onChange={onFilesChosen}
      />
      <SharePointFilePicker
        open={pickerOpen}
        webUrl={context.pageContext.web.absoluteUrl}
        filters={SUPPORTED_EXTENSIONS}
        getToken={getToken}
        onPick={onSharePointPicked}
        onClose={() => setPickerOpen(false)}
      />
    </div>
  );
};

export const HelloCopilot: React.FC<Props> = ({ theme, ...props }) => {
  const styles = useStyles();
  return (
    <FluentProvider theme={theme} className={styles.provider}>
      <Chat key={JSON.stringify([props.apiUrl, props.apiResource, props.companionBaseUrl])} {...props} />
    </FluentProvider>
  );
};
