import * as React from 'react';
import {
  Badge,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Link,
  makeStyles,
  Menu,
  MenuDivider,
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
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
  TableSelectionCell,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  tokens,
  Tooltip,
  typographyStyles,
  useTableCompositeNavigation
} from '@fluentui/react-components';
import {
  ArrowClockwise20Regular,
  ArrowUpload20Regular,
  ChevronDown16Regular,
  ChevronLeft16Regular,
  ChevronRight16Regular,
  Compose20Regular,
  Delete20Regular,
  Dismiss20Regular,
  Globe20Regular,
  Open20Regular
} from '@fluentui/react-icons';
import { MAX_FILE_BYTES, SUPPORTED_EXTENSIONS, UserDocument } from './api';
import { safeSourceUrl, sourceLabel } from './citationHelpers';
import { FileTypeIcon } from './DocumentSet';
import { DocumentErrors, DocumentsState, formatSize, statusBadge } from './useDocuments';

type SortColumn = 'name' | 'source' | 'status' | 'size' | 'added' | 'usedIn';
type SortDirection = 'ascending' | 'descending';
const PAGE_SIZE_OPTIONS = [10, 25, 50] as const;
const STATUS_RANK: Record<UserDocument['status'], number> = { ready: 0, processing: 1, failed: 2 };

const useLibraryStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0,
    height: '100%'
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minHeight: '48px',
    flexShrink: 0,
    padding: `0 ${tokens.spacingHorizontalS} 0 ${tokens.spacingHorizontalL}`
  },
  title: {
    ...typographyStyles.subtitle2,
    margin: 0,
    flexGrow: 1
  },
  commandBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    minHeight: '44px',
    flexShrink: 0,
    padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalL}`,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2
  },
  toolbar: {
    flexWrap: 'wrap',
    padding: 0,
    gap: tokens.spacingHorizontalXXS
  },
  toolbarStatus: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground2
  },
  notices: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL} 0`,
    '&:empty': {
      display: 'none'
    }
  },
  body: {
    flexGrow: 1,
    overflowY: 'auto',
    scrollbarWidth: 'thin',
    padding: `0 ${tokens.spacingHorizontalL} ${tokens.spacingVerticalM}`
  },
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
    minHeight: '44px',
    flexShrink: 0,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalL}`,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground2
  },
  paginationControls: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS
  },
  pageIndicator: {
    padding: `0 ${tokens.spacingHorizontalXS}`,
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorNeutralForeground1,
    ...typographyStyles.caption1Strong
  },
  nameCell: {
    flex: '1 1 180px',
    minWidth: 0
  },
  colSource: {
    flex: '0 0 96px'
  },
  colSourceCompact: {
    flex: '0 0 72px'
  },
  colStatus: {
    flex: '0 0 128px'
  },
  colSize: {
    flex: '0 0 88px'
  },
  colAdded: {
    flex: '0 0 120px'
  },
  colUsedIn: {
    flex: '0 0 24%',
    minWidth: 0
  },
  usedIn: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    ...typographyStyles.caption1
  },
  usedInLink: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  muted: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums'
  },
  empty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalL}`,
    textAlign: 'center',
    color: tokens.colorNeutralForeground2
  },
  emptyTitle: {
    ...typographyStyles.subtitle2,
    margin: 0,
    color: tokens.colorNeutralForeground1
  },
  emptyText: {
    margin: 0,
    maxWidth: '48ch'
  },
  emptyActions: {
    display: 'flex',
    flexWrap: 'wrap',
    justifyContent: 'center',
    gap: tokens.spacingHorizontalS
  },
  menuList: {
    maxHeight: '320px',
    overflowY: 'auto',
    overflowX: 'hidden',
    maxWidth: '360px'
  },
  deleteText: {
    overflowWrap: 'anywhere'
  },
  selectedCount: {
    ...typographyStyles.caption1Strong,
    color: tokens.colorNeutralForeground1,
    fontVariantNumeric: 'tabular-nums'
  },
  row: {
    cursor: 'pointer'
  }
});

export interface LibraryChat { id: string; title: string; documentIds: readonly string[]; }
export interface LibraryTarget { chatId: string; title: string; }

interface Props {
  state: DocumentsState;
  chats: readonly LibraryChat[];
  target?: LibraryTarget;
  busy: boolean;
  /** Width available to the Library, which decides how many columns fit. */
  width: number;
  sidebarToggle?: React.ReactNode;
  onUpload: () => void;
  onPickSharePoint: () => void;
  onAttach: (chatId: string | undefined, documentIds: string[]) => void;
  onCancelTarget: () => void;
  onOpenChat: (chatId: string) => void;
}

const formatDate = (iso: string): string => {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
};

/** All of the user's documents; select some and attach them to a new or existing conversation. */
export const Library: React.FC<Props> = ({
  state, chats, target, busy, width, sidebarToggle, onUpload, onPickSharePoint, onAttach, onCancelTarget, onOpenChat
}) => {
  const styles = useLibraryStyles();
  const { tableTabsterAttribute, tableRowTabsterAttribute, onTableKeyDown } = useTableCompositeNavigation();
  const compact = width > 0 && width < 760;
  const showSizeAndDate = width >= 1100;
  const [selected, setSelected] = React.useState<string[]>([]);
  const [confirmDelete, setConfirmDelete] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);
  const [refreshing, setRefreshing] = React.useState(false);
  const [sortColumn, setSortColumn] = React.useState<SortColumn>('added');
  const [sortDirection, setSortDirection] = React.useState<SortDirection>('descending');
  const [page, setPage] = React.useState(0);
  const [pageSize, setPageSize] = React.useState<number>(10);
  const documents = state.documents;

  // Drop selections for documents that disappeared (deleted elsewhere or removed after a failed upload).
  React.useEffect(() => {
    setSelected(current => current.filter(id => documents.some(document => document.id === id)));
  }, [documents]);

  // When opened for a specific conversation, start with nothing selected.
  React.useEffect(() => { setSelected([]); }, [target?.chatId]);

  const usedIn = React.useCallback((id: string): LibraryChat[] =>
    chats.filter(chat => chat.documentIds.indexOf(id) >= 0), [chats]);

  const sortedDocuments = React.useMemo(() => {
    const multiplier = sortDirection === 'ascending' ? 1 : -1;
    return [...documents].sort((a, b) => {
      let cmp = 0;
      switch (sortColumn) {
        case 'name':
          cmp = a.fileName.localeCompare(b.fileName, undefined, { numeric: true, sensitivity: 'base' });
          break;
        case 'source':
          cmp = sourceLabel(a.sourceType).localeCompare(sourceLabel(b.sourceType));
          break;
        case 'status':
          cmp = STATUS_RANK[a.status] - STATUS_RANK[b.status];
          break;
        case 'size':
          cmp = a.sizeBytes - b.sizeBytes;
          break;
        case 'added': {
          const timeA = Date.parse(a.createdAt);
          const timeB = Date.parse(b.createdAt);
          cmp = (Number.isNaN(timeA) ? 0 : timeA) - (Number.isNaN(timeB) ? 0 : timeB);
          break;
        }
        case 'usedIn': {
          const chatsA = usedIn(a.id);
          const chatsB = usedIn(b.id);
          cmp = chatsA.length - chatsB.length || (chatsA[0]?.title ?? '').localeCompare(chatsB[0]?.title ?? '');
          break;
        }
      }
      if (cmp !== 0) return cmp * multiplier;
      return a.fileName.localeCompare(b.fileName, undefined, { numeric: true, sensitivity: 'base' });
    });
  }, [documents, sortColumn, sortDirection, usedIn]);

  const totalPages = Math.max(1, Math.ceil(sortedDocuments.length / pageSize));
  const safePage = Math.min(page, totalPages - 1);

  React.useEffect(() => {
    if (page !== safePage) setPage(safePage);
  }, [page, safePage]);

  const pagedDocuments = React.useMemo(
    () => sortedDocuments.slice(safePage * pageSize, (safePage + 1) * pageSize),
    [sortedDocuments, safePage, pageSize]
  );

  const toggleSort = (column: SortColumn, defaultDirection: SortDirection = 'ascending'): void => {
    if (sortColumn === column) {
      setSortDirection(current => current === 'ascending' ? 'descending' : 'ascending');
    } else {
      setSortColumn(column);
      setSortDirection(defaultDirection);
    }
    setPage(0);
  };

  const sortDirectionOf = (column: SortColumn): SortDirection | undefined =>
    sortColumn === column ? sortDirection : undefined;

  const toggle = (id: string): void =>
    setSelected(current => current.indexOf(id) >= 0 ? current.filter(item => item !== id) : [...current, id]);

  const allSelected = documents.length > 0 && selected.length === documents.length;
  const attachable = selected.filter(id => documents.find(document => document.id === id)?.status !== 'failed');
  const targetChat = target && chats.find(chat => chat.id === target.chatId);
  const newForTarget = targetChat ? attachable.filter(id => targetChat.documentIds.indexOf(id) < 0) : attachable;
  const singleSelectedDoc = selected.length === 1 ? documents.find(document => document.id === selected[0]) : undefined;
  const singleSelectedUrl = safeSourceUrl(singleSelectedDoc?.sourceUrl);

  const remove = async (): Promise<void> => {
    setDeleting(true);
    for (const id of selected) await state.remove(id);
    setDeleting(false);
    setConfirmDelete(false);
    setSelected([]);
  };

  const refreshLibrary = async (): Promise<void> => {
    setRefreshing(true);
    try { await state.refresh(); }
    finally { setRefreshing(false); }
  };

  const rangeStart = sortedDocuments.length === 0 ? 0 : safePage * pageSize + 1;
  const rangeEnd = Math.min(sortedDocuments.length, (safePage + 1) * pageSize);

  return (
    <section className={styles.root} aria-label="Library">
      <div className={styles.header}>
        {sidebarToggle}
        <h2 className={styles.title}>Library</h2>
      </div>

      <div className={styles.commandBar}>
        <Toolbar className={styles.toolbar} aria-label="Library actions" size="small">
          <Tooltip content={`Upload ${SUPPORTED_EXTENSIONS.join(', ')} up to ${MAX_FILE_BYTES / (1024 * 1024)} MB`} relationship="description" withArrow>
            <ToolbarButton appearance="subtle" icon={<ArrowUpload20Regular />} disabled={busy} onClick={onUpload} aria-label={compact ? 'Upload' : undefined}>
              {!compact && 'Upload'}
            </ToolbarButton>
          </Tooltip>
          <ToolbarButton appearance="subtle" icon={<Globe20Regular />} disabled={busy} onClick={onPickSharePoint} aria-label={compact ? 'From SharePoint' : undefined}>
            {!compact && 'From SharePoint'}
          </ToolbarButton>
          <ToolbarButton appearance="subtle" icon={refreshing ? <Spinner size="extra-tiny" /> : <ArrowClockwise20Regular />}
            disabled={busy || refreshing || !state.loaded} onClick={() => { void refreshLibrary(); }} aria-label={compact ? 'Refresh' : undefined}>
            {!compact && 'Refresh'}
          </ToolbarButton>

          <ToolbarDivider />

          {target ? (
            <ToolbarButton appearance="primary" icon={<Compose20Regular />} disabled={busy || newForTarget.length === 0}
              onClick={() => onAttach(target.chatId, newForTarget)}>
              {newForTarget.length ? `Add ${newForTarget.length} to chat` : 'Add to chat'}
            </ToolbarButton>
          ) : (
            <Menu positioning="below-start">
              <MenuTrigger disableButtonEnhancement>
                <ToolbarButton appearance={attachable.length > 0 ? 'primary' : 'subtle'}
                  icon={<Compose20Regular />} disabled={busy || attachable.length === 0}>
                  {attachable.length > 0 ? `Attach to chat (${attachable.length})` : 'Attach to chat'}
                  <ChevronDown16Regular />
                </ToolbarButton>
              </MenuTrigger>
              <MenuPopover>
                <MenuList className={styles.menuList}>
                  <MenuItem icon={<Compose20Regular />} onClick={() => onAttach(undefined, attachable)}>New chat</MenuItem>
                  {chats.length > 0 && <MenuDivider />}
                  {chats.length > 0 && (
                    <MenuGroup>
                      <MenuGroupHeader>Existing chats</MenuGroupHeader>
                      {chats.map(chat => {
                        const missing = attachable.filter(id => chat.documentIds.indexOf(id) < 0);
                        return (
                          <MenuItem
                            key={chat.id}
                            disabled={missing.length === 0}
                            secondaryContent={missing.length === 0 ? 'Already added'
                              : chat.documentIds.length ? `${chat.documentIds.length} document${chat.documentIds.length === 1 ? '' : 's'}` : 'General chat'}
                            onClick={() => onAttach(chat.id, missing)}
                          >
                            {chat.title}
                          </MenuItem>
                        );
                      })}
                    </MenuGroup>
                  )}
                </MenuList>
              </MenuPopover>
            </Menu>
          )}

          <ToolbarButton appearance="subtle" icon={<Open20Regular />} disabled={!singleSelectedUrl}
            onClick={() => { if (singleSelectedUrl) window.open(singleSelectedUrl, '_blank', 'noopener,noreferrer'); }}
            aria-label={compact ? 'Open source' : undefined}>
            {!compact && 'Open'}
          </ToolbarButton>

          <ToolbarButton appearance="subtle" icon={<Delete20Regular />} disabled={busy || selected.length === 0}
            onClick={() => setConfirmDelete(true)} aria-label={compact ? 'Delete' : undefined}>
            {!compact && (selected.length > 0 ? `Delete (${selected.length})` : 'Delete')}
          </ToolbarButton>

          <ToolbarButton appearance="subtle" icon={<Dismiss20Regular />} disabled={selected.length === 0}
            onClick={() => setSelected([])} aria-label={compact ? 'Clear selection' : undefined}>
            {!compact && 'Clear selection'}
          </ToolbarButton>
        </Toolbar>

        <div className={styles.toolbarStatus}>
          {selected.length > 0 && attachable.length < selected.length && (
            <span className={styles.muted}>Failed documents can&apos;t be attached.</span>
          )}
          <span className={styles.selectedCount} aria-live="polite">
            {selected.length > 0
              ? `${selected.length} of ${documents.length} selected`
              : `${documents.length} ${documents.length === 1 ? 'document' : 'documents'}`}
          </span>
        </div>
      </div>

      <div className={styles.notices}>
        <DocumentErrors state={state} />
        {target && (
          <MessageBar intent="info" layout="multiline">
            <MessageBarBody>Select documents to add to &quot;{target.title}&quot;.</MessageBarBody>
            <MessageBarActions>
              <Button size="small" appearance="primary" disabled={busy || newForTarget.length === 0}
                onClick={() => onAttach(target.chatId, newForTarget)}>
                {newForTarget.length ? `Add ${newForTarget.length} to chat` : 'Add to chat'}
              </Button>
              <Button size="small" onClick={onCancelTarget}>Cancel</Button>
            </MessageBarActions>
          </MessageBar>
        )}
      </div>

      <div className={styles.body}>
        {!state.loaded ? <Spinner size="small" label="Loading documents…" /> : documents.length === 0 ? (
          <div className={styles.empty}>
            <h3 className={styles.emptyTitle}>Your library is empty</h3>
            <p className={styles.emptyText}>
              Add files from this device or SharePoint, then attach them to a chat. Only you can search your documents.
              Supported: {SUPPORTED_EXTENSIONS.join(', ')} up to {MAX_FILE_BYTES / (1024 * 1024)} MB.
            </p>
            <div className={styles.emptyActions}>
              <Button appearance="primary" icon={<ArrowUpload20Regular />} disabled={busy} onClick={onUpload}>Upload</Button>
              <Button icon={<Globe20Regular />} disabled={busy} onClick={onPickSharePoint}>From SharePoint</Button>
            </div>
          </div>
        ) : (
          <Table {...tableTabsterAttribute} onKeyDown={onTableKeyDown} noNativeElements sortable aria-label="Documents" size="medium">
            <TableHeader>
              <TableRow>
                <TableSelectionCell
                  checked={allSelected ? true : selected.length > 0 ? 'mixed' : false}
                  onClick={() => setSelected(allSelected ? [] : documents.map(document => document.id))}
                  checkboxIndicator={{ 'aria-label': 'Select all documents' }}
                />
                <TableHeaderCell className={styles.nameCell} sortDirection={sortDirectionOf('name')}
                  onClick={() => toggleSort('name', 'ascending')}>
                  Name
                </TableHeaderCell>
                <TableHeaderCell className={compact ? styles.colSourceCompact : styles.colSource} sortDirection={sortDirectionOf('source')}
                  onClick={() => toggleSort('source', 'ascending')}>
                  Source
                </TableHeaderCell>
                {!compact && (
                  <TableHeaderCell className={styles.colStatus} sortDirection={sortDirectionOf('status')}
                    onClick={() => toggleSort('status', 'ascending')}>
                    Status
                  </TableHeaderCell>
                )}
                {showSizeAndDate && (
                  <TableHeaderCell className={styles.colSize} sortDirection={sortDirectionOf('size')}
                    onClick={() => toggleSort('size', 'descending')}>
                    Size
                  </TableHeaderCell>
                )}
                {showSizeAndDate && (
                  <TableHeaderCell className={styles.colAdded} sortDirection={sortDirectionOf('added')}
                    onClick={() => toggleSort('added', 'descending')}>
                    Added
                  </TableHeaderCell>
                )}
                {!compact && (
                  <TableHeaderCell className={styles.colUsedIn} sortDirection={sortDirectionOf('usedIn')}
                    onClick={() => toggleSort('usedIn', 'descending')}>
                    Used in
                  </TableHeaderCell>
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {pagedDocuments.map(document => {
                const isSelected = selected.indexOf(document.id) >= 0;
                const url = safeSourceUrl(document.sourceUrl);
                const chatsUsing = usedIn(document.id);
                return (
                  <TableRow {...tableRowTabsterAttribute} key={document.id} className={styles.row} aria-selected={isSelected} appearance={isSelected ? 'brand' : 'none'}
                    onClick={() => toggle(document.id)}
                    onKeyDown={e => { if (e.key === ' ' && e.target === e.currentTarget) { e.preventDefault(); toggle(document.id); } }}>
                    <TableSelectionCell checked={isSelected} checkboxIndicator={{ 'aria-label': `Select ${document.fileName}` }} />
                    <TableCell className={styles.nameCell}>
                      <TableCellLayout truncate media={<FileTypeIcon fileName={document.fileName} size={24} />}
                        description={compact ? statusBadge(document) : document.status === 'failed' ? document.error : undefined}>
                        {url
                          ? <Link href={url} target="_blank" rel="noopener noreferrer" onClick={e => e.stopPropagation()}>{document.fileName}</Link>
                          : document.fileName}
                      </TableCellLayout>
                    </TableCell>
                    <TableCell className={compact ? styles.colSourceCompact : styles.colSource}>
                      <Badge appearance="tint" size="small" color={document.sourceType === 'sharepoint' ? 'brand' : 'informative'}>{sourceLabel(document.sourceType)}</Badge>
                    </TableCell>
                    {!compact && <TableCell className={styles.colStatus}>{statusBadge(document)}</TableCell>}
                    {showSizeAndDate && <TableCell className={styles.colSize}><span className={styles.muted}>{formatSize(document.sizeBytes)}</span></TableCell>}
                    {showSizeAndDate && <TableCell className={styles.colAdded}><span className={styles.muted}>{formatDate(document.createdAt)}</span></TableCell>}
                    {!compact && (
                      <TableCell className={styles.colUsedIn}>
                        {chatsUsing.length === 0 ? <span className={styles.muted}>Not in a chat</span> : (
                          <span className={styles.usedIn}>
                            <Link className={styles.usedInLink} title={chatsUsing[0].title}
                              onClick={e => { e.stopPropagation(); onOpenChat(chatsUsing[0].id); }}>
                              {chatsUsing[0].title}
                            </Link>
                            {chatsUsing.length > 1 && (
                              <Tooltip content={chatsUsing.slice(1).map(chat => chat.title).join(', ')} relationship="description" withArrow>
                                <span className={styles.muted}>+{chatsUsing.length - 1}</span>
                              </Tooltip>
                            )}
                          </span>
                        )}
                      </TableCell>
                    )}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </div>

      {state.loaded && documents.length > 0 && (
        <div className={styles.footer} role="navigation" aria-label="Table pagination">
          <span className={styles.muted}>
            Showing {rangeStart}–{rangeEnd} of {sortedDocuments.length}
          </span>
          <div className={styles.paginationControls}>
            <Menu
              positioning="above-end"
              checkedValues={{ pageSize: [String(pageSize)] }}
              onCheckedValueChange={(_, data) => {
                const next = Number(data.checkedItems[0]);
                if (next) { setPageSize(next); setPage(0); }
              }}
            >
              <MenuTrigger disableButtonEnhancement>
                <Button appearance="subtle" size="small" icon={<ChevronDown16Regular />} iconPosition="after">
                  {pageSize} per page
                </Button>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuGroup>
                    <MenuGroupHeader>Rows per page</MenuGroupHeader>
                    {PAGE_SIZE_OPTIONS.map(option => (
                      <MenuItemRadio key={option} name="pageSize" value={String(option)}>
                        {option} rows
                      </MenuItemRadio>
                    ))}
                  </MenuGroup>
                </MenuList>
              </MenuPopover>
            </Menu>
            <Tooltip content="Previous page" relationship="label" withArrow>
              <Button appearance="subtle" size="small" icon={<ChevronLeft16Regular />}
                disabled={safePage <= 0} onClick={() => setPage(current => Math.max(0, current - 1))} />
            </Tooltip>
            <span className={styles.pageIndicator} aria-live="polite">
              Page {safePage + 1} of {totalPages}
            </span>
            <Tooltip content="Next page" relationship="label" withArrow>
              <Button appearance="subtle" size="small" icon={<ChevronRight16Regular />}
                disabled={safePage >= totalPages - 1} onClick={() => setPage(current => Math.min(totalPages - 1, current + 1))} />
            </Tooltip>
          </div>
        </div>
      )}

      <Dialog open={confirmDelete} onOpenChange={(_, data) => { if (!deleting) setConfirmDelete(data.open); }}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete {selected.length === 1 ? 'this document' : `${selected.length} documents`}?</DialogTitle>
            <DialogContent className={styles.deleteText}>
              {selected.length === 1 ? 'It is' : 'They are'} removed from your library and no longer searched in any chat.
              Earlier answers keep their quoted excerpts. This can&apos;t be undone.
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={deleting} onClick={() => setConfirmDelete(false)}>Cancel</Button>
              <Button appearance="primary" disabled={deleting} icon={deleting ? <Spinner size="tiny" /> : undefined} onClick={() => { void remove(); }}>
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </section>
  );
};
