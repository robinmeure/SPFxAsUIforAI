import * as React from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner
} from '@fluentui/react-components';
import { Dismiss16Regular } from '@fluentui/react-icons';
import { ChatApi, MAX_FILE_BYTES, SUPPORTED_EXTENSIONS, UserDocument } from './api';
import { PickedFile } from './FilePicker';
import { useStyles } from './HelloCopilot.styles';

const POLL_MS = 3000;

const extensionOf = (name: string): string => {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
};

export const formatSize = (bytes: number): string =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

export interface DocumentsState {
  documents: UserDocument[];
  loaded: boolean;
  errors: string[];
  dismissError: (index: number) => void;
  refresh: () => Promise<void>;
  upload: (files: File[]) => Promise<UserDocument[]>;
  addFromSharePoint: (files: PickedFile[]) => Promise<UserDocument[]>;
  remove: (id: string) => Promise<void>;
}

/** Loads the user's documents and polls while any of them is still being processed. */
export const useDocuments = (api: ChatApi): DocumentsState => {
  const [documents, setDocuments] = React.useState<UserDocument[]>([]);
  const [loaded, setLoaded] = React.useState(false);
  const [errors, setErrors] = React.useState<string[]>([]);
  const addError = (message: string): void => setErrors(current => [...current, message]);

  const refresh = React.useCallback(async (): Promise<void> => {
    if (!api.hasCompanions) return;
    try {
      setDocuments(await api.listDocuments());
    } catch (e) {
      addError(e instanceof Error ? e.message : 'Couldn\'t load your documents.');
    } finally {
      setLoaded(true);
    }
  }, [api]);

  React.useEffect(() => { void refresh(); }, [refresh]);

  const processing = documents.some(document => document.status === 'processing');
  React.useEffect(() => {
    if (!processing) return undefined;
    const timer = window.setInterval(() => {
      api.listDocuments().then(setDocuments).catch(() => { /* Keep the last known state; the next poll retries. */ });
    }, POLL_MS);
    return () => window.clearInterval(timer);
  }, [api, processing]);

  const added = (document: UserDocument): void =>
    setDocuments(current => [document, ...current.filter(existing => existing.id !== document.id)]);

  const upload = async (files: File[]): Promise<UserDocument[]> => {
    const results: UserDocument[] = [];
    for (const file of files) {
      if (SUPPORTED_EXTENSIONS.indexOf(extensionOf(file.name)) < 0) {
        addError(`"${file.name}" isn't a supported file type.`);
      } else if (file.size > MAX_FILE_BYTES) {
        addError(`"${file.name}" is larger than ${MAX_FILE_BYTES / (1024 * 1024)} MB.`);
      } else {
        try { const document = await api.uploadDocument(file); added(document); results.push(document); }
        catch (e) { addError(`"${file.name}": ${e instanceof Error ? e.message : 'upload failed.'}`); }
      }
    }
    return results;
  };

  const addFromSharePoint = async (files: PickedFile[]): Promise<UserDocument[]> => {
    const results: UserDocument[] = [];
    for (const file of files) {
      try { const document = await api.addSharePointDocument(file.driveId, file.itemId); added(document); results.push(document); }
      catch (e) { addError(`"${file.name}": ${e instanceof Error ? e.message : 'couldn\'t be added.'}`); }
    }
    return results;
  };

  const remove = async (id: string): Promise<void> => {
    try {
      await api.deleteDocument(id);
      setDocuments(current => current.filter(document => document.id !== id));
    } catch (e) {
      addError(e instanceof Error ? e.message : 'Couldn\'t remove the document.');
    }
  };

  return {
    documents, loaded, errors, refresh, upload, addFromSharePoint, remove,
    dismissError: index => setErrors(current => current.filter((_, i) => i !== index))
  };
};

export const DocumentErrors: React.FC<{ state: DocumentsState }> = ({ state }) => {
  const styles = useStyles();
  if (!state.errors.length) return null;
  return (
    <div className={styles.documentErrors}>
      {state.errors.map((error, index) => (
        <MessageBar key={index} intent="error" layout="multiline">
          <MessageBarBody>{error}</MessageBarBody>
          <MessageBarActions containerAction={
            <Button appearance="transparent" size="small" aria-label="Dismiss" icon={<Dismiss16Regular />} onClick={() => state.dismissError(index)} />
          } />
        </MessageBar>
      ))}
    </div>
  );
};

export const statusBadge = (document: UserDocument): React.ReactElement => {
  switch (document.status) {
    case 'ready': return <Badge appearance="tint" color="success" size="small">Ready</Badge>;
    case 'failed': return <Badge appearance="tint" color="danger" size="small">Failed</Badge>;
    default: return <Badge appearance="tint" color="informative" size="small" icon={<Spinner size="extra-tiny" />}>Processing</Badge>;
  }
};

