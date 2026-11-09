import * as React from 'react';
import {
  Badge,
  Button,
  InteractionTag,
  InteractionTagPrimary,
  InteractionTagSecondary,
  Link,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Spinner,
  TagGroup,
  Tooltip,
  tokens
} from '@fluentui/react-components';
import { Add16Regular, Dismiss16Regular } from '@fluentui/react-icons';
import { UserDocument } from './api';
import { safeSourceUrl, sourceLabel } from './citationHelpers';
import { useStyles } from './HelloCopilot.styles';

export interface SetConflict { chatId: string; title: string; }

const FILE_TYPES: Record<string, [string, string]> = {
  '.docx': ['W', '#185abd'], '.pdf': ['P', '#c4314b'], '.xlsx': ['X', '#107c41'], '.pptx': ['P', '#c43e1c'],
  '.md': ['M', tokens.colorNeutralForeground3], '.txt': ['T', tokens.colorNeutralForeground3],
  '.html': ['H', tokens.colorNeutralForeground3], '.htm': ['H', tokens.colorNeutralForeground3]
};

const extensionOf = (name: string): string => {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
};

export const sameSet = (left: readonly string[], right: readonly string[]): boolean =>
  left.length === right.length && left.every(id => right.indexOf(id) >= 0);

/** File type by colour and letter, matching the Office file-type convention. */
export const FileTypeIcon: React.FC<{ fileName?: string; size?: number }> = ({ fileName, size = 20 }) => {
  const [letter, color] = (fileName && FILE_TYPES[extensionOf(fileName)]) || ['?', tokens.colorNeutralForeground3];
  return (
    <svg width={Math.round(size * 20 / 24)} height={size} viewBox="0 0 20 24" aria-hidden="true" focusable="false" style={{ flexShrink: 0, display: 'block' }}>
      <path d="M2 1.5h11l5.5 5.5v15.5H2z" fill={tokens.colorNeutralBackground1} stroke={color} strokeOpacity=".5" />
      <path d="M13 1.5V7h5.5" fill="none" stroke={color} strokeOpacity=".5" />
      <rect x="0" y="11" width="13" height="10" rx="2" fill={color} />
      <text x="6.5" y="19" textAnchor="middle" fontFamily={tokens.fontFamilyBase} fontSize="8" fontWeight="700" fill={tokens.colorNeutralForegroundOnBrand}>{letter}</text>
    </svg>
  );
};

const resolve = (ids: readonly string[], documents: readonly UserDocument[]): (UserDocument | undefined)[] =>
  ids.map(id => documents.find(document => document.id === id));

const statusLabel = (document: UserDocument): string =>
  document.status === 'ready' ? `Ready${document.chunkCount ? ` · ${document.chunkCount} passages` : ''}`
    : document.status === 'failed' ? 'Failed: not searched' : 'Processing: searched when ready';

interface PillProps {
  ids: readonly string[];
  documents: readonly UserDocument[];
  busy: boolean;
  onRemove: (id: string) => void;
  onAdd: () => void;
}

/** Header control: the conversation's document set, editable from its popover. */
export const DocumentSetPill: React.FC<PillProps> = ({ ids, documents, busy, onRemove, onAdd }) => {
  const styles = useStyles();
  const [open, setOpen] = React.useState(false);
  const items = resolve(ids, documents);
  const label = ids.length === 0 ? 'Add documents' : `${ids.length} document${ids.length === 1 ? '' : 's'}`;
  const accessibleLabel = ids.length === 0 ? 'No documents in this conversation. Add documents' : `${label} in this conversation`;
  const leadingIcon = ids.length === 0 ? <Add16Regular /> : (
    <span className={styles.iconStack}>
      {items.slice(0, 3).map((document, index) => <FileTypeIcon key={ids[index]} fileName={document?.fileName} size={18} />)}
    </span>
  );
  return (
    <Popover open={open} onOpenChange={(_, data) => setOpen(data.open)} positioning="below-end" trapFocus>
      <PopoverTrigger disableButtonEnhancement>
        <Button appearance="outline" shape="circular" size="small" className={styles.setPill}
          icon={leadingIcon} aria-label={accessibleLabel}>
          {label}
          {busy && <Spinner size="extra-tiny" aria-hidden="true" />}
        </Button>
      </PopoverTrigger>
      <PopoverSurface className={styles.setSurface} aria-label="Documents in this conversation">
        <h3 className={styles.setHeading}>Documents in this conversation</h3>
        {ids.length === 0 && <p className={styles.setEmpty}>This is a general chat. Add documents to search them.</p>}
        <ul className={styles.setList}>
          {items.map((document, index) => {
            const url = safeSourceUrl(document?.sourceUrl);
            return (
              <li key={ids[index]} className={styles.setItem}>
                <FileTypeIcon fileName={document?.fileName} size={26} />
                <span className={styles.setText}>
                  <span className={styles.setName} title={document?.fileName}>
                    {!document ? 'Unavailable document' : url
                      ? <Link href={url} target="_blank" rel="noopener noreferrer">{document.fileName}</Link> : document.fileName}
                  </span>
                  <span className={styles.setMeta}>{document ? statusLabel(document) : 'Deleted or no longer accessible'}</span>
                </span>
                {document && <Badge appearance="tint" size="small" color={document.sourceType === 'sharepoint' ? 'brand' : 'informative'}>{sourceLabel(document.sourceType)}</Badge>}
                <Tooltip content={`Remove ${document?.fileName ?? 'unavailable document'} from this conversation`} relationship="label" withArrow>
                  <Button appearance="subtle" size="small" icon={<Dismiss16Regular />} disabled={busy} onClick={() => onRemove(ids[index])} />
                </Tooltip>
              </li>
            );
          })}
        </ul>
        <div className={styles.setFoot}>
          <span>Answers search only these documents. Changes apply from the next message and are marked in the conversation.</span>
          <Button size="small" icon={<Add16Regular />} disabled={busy || ids.length >= 20} onClick={() => { setOpen(false); onAdd(); }}>Add from Library</Button>
        </div>
      </PopoverSurface>
    </Popover>
  );
};

interface DraftProps {
  ids: readonly string[];
  documents: readonly UserDocument[];
  disabled: boolean;
  onRemove: (id: string) => void;
}

/** Documents chosen for a chat that hasn't been sent yet, shown inside the message box. */
export const DraftAttachments: React.FC<DraftProps> = ({ ids, documents, disabled, onRemove }) => {
  const styles = useStyles();
  if (!ids.length) return null;
  return (
    <TagGroup
      className={styles.draftList}
      size="small"
      disabled={disabled}
      aria-label="Documents for this chat"
      onDismiss={(_, data) => onRemove(data.value)}
    >
      {resolve(ids, documents).map((document, index) => {
        const id = ids[index];
        const name = document?.fileName ?? 'Unavailable document';
        return (
          <InteractionTag key={id} value={id} appearance="filled" size="small" title={name}>
            <InteractionTagPrimary
              hasSecondaryAction
              icon={document?.status === 'processing'
                ? <Spinner size="extra-tiny" aria-label="Processing" />
                : <FileTypeIcon fileName={document?.fileName} size={16} />}
            >
              <span className={styles.tagContent}>{name}</span>
            </InteractionTagPrimary>
            <InteractionTagSecondary aria-label={`Remove ${name}`} />
          </InteractionTag>
        );
      })}
    </TagGroup>
  );
};