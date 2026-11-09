import * as React from 'react';
import { Button, Dialog, DialogBody, DialogContent, DialogSurface, DialogTitle, MessageBar, MessageBarBody, Spinner } from '@fluentui/react-components';
import { Dismiss20Regular } from '@fluentui/react-icons';
import { useStyles } from './HelloCopilot.styles';

export interface PickedFile { driveId: string; itemId: string; name: string; size?: number; }

interface PickerProps {
  open: boolean;
  /** Site the picker opens in; its host also serves the picker page. */
  webUrl: string;
  filters: string[];
  getToken: (resource: string) => Promise<string>;
  onPick: (files: PickedFile[]) => void;
  onClose: () => void;
}

interface PickerItem { id: string; name?: string; size?: number; parentReference?: { driveId?: string }; }
interface PickerCommand { command: string; resource?: string; items?: PickerItem[]; }
interface PortMessage { type: string; id?: string; data?: PickerCommand; }

const newChannelId = (): string => (window.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`);

/**
 * Microsoft File Picker v8 hosted in an iframe. The host posts the picker options with an access token to
 * FilePicker.aspx, then answers the picker's commands (authenticate, pick, close) over a MessagePort.
 * See https://learn.microsoft.com/onedrive/developer/controls/file-pickers/.
 */
export const SharePointFilePicker: React.FC<PickerProps> = ({ open, webUrl, filters, getToken, onPick, onClose }) => {
  const styles = useStyles();
  const [frame, setFrame] = React.useState<HTMLIFrameElement | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string>();
  const callbacks = React.useRef({ getToken, onPick, onClose });
  callbacks.current = { getToken, onPick, onClose };

  React.useEffect(() => {
    if (!open || !frame) return undefined;
    setLoading(true);
    setError(undefined);
    const channelId = newChannelId();
    const origin = new URL(webUrl).origin;
    let port: MessagePort | undefined;
    let disposed = false;

    const reply = (id: string | undefined, data: Record<string, unknown>): void => port?.postMessage({ type: 'result', id, data });

    const onPortMessage = async (event: MessageEvent<PortMessage>): Promise<void> => {
      const message = event.data;
      if (message.type !== 'command' || !message.data) return;
      port?.postMessage({ type: 'acknowledge', id: message.id });
      const command = message.data;
      try {
        switch (command.command) {
          case 'authenticate':
            reply(message.id, { result: 'token', token: await callbacks.current.getToken(new URL(command.resource ?? origin).origin) });
            break;
          case 'pick': {
            const files = (command.items ?? [])
              .filter(item => item.parentReference?.driveId)
              .map(item => ({ driveId: item.parentReference!.driveId!, itemId: item.id, name: item.name ?? item.id, size: item.size }));
            reply(message.id, { result: 'success' });
            callbacks.current.onPick(files);
            callbacks.current.onClose();
            break;
          }
          case 'close':
            reply(message.id, { result: 'success' });
            callbacks.current.onClose();
            break;
          default:
            reply(message.id, { result: 'error', error: { code: 'unsupportedCommand', message: command.command } });
        }
      } catch (e) {
        reply(message.id, { result: 'error', error: { code: 'unableToObtainToken', message: e instanceof Error ? e.message : 'Token error' } });
      }
    };

    const onWindowMessage = (event: MessageEvent): void => {
      const data = event.data as { type?: string; channelId?: string } | undefined;
      if (event.source !== frame.contentWindow || event.origin !== origin || data?.type !== 'initialize' || data.channelId !== channelId) return;
      port = event.ports[0];
      port.addEventListener('message', e => { void onPortMessage(e as MessageEvent<PortMessage>); });
      port.start();
      port.postMessage({ type: 'activate' });
      setLoading(false);
    };
    window.addEventListener('message', onWindowMessage);

    const options = {
      sdk: '8.0',
      entry: { sharePoint: { byPath: { web: webUrl } } },
      authentication: {},
      messaging: { origin: window.location.origin, channelId },
      typesAndSources: { mode: 'files', filters, pivots: { oneDrive: true, recent: true, sharedLibraries: true, site: true } },
      selection: { mode: 'multiple', maximumCount: 10 }
    };

    void (async () => {
      try {
        const token = await callbacks.current.getToken(origin);
        if (disposed) return;
        // POST the token into the iframe so it never appears in a URL.
        const form = document.createElement('form');
        form.method = 'POST';
        form.target = frame.name;
        form.action = `${webUrl.replace(/\/+$/, '')}/_layouts/15/FilePicker.aspx?${new URLSearchParams({ filePicker: JSON.stringify(options) }).toString()}`;
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'access_token';
        input.value = token;
        form.appendChild(input);
        document.body.appendChild(form);
        form.submit();
        form.remove();
      } catch {
        if (!disposed) {
          setLoading(false);
          setError('Couldn\'t open SharePoint. Check that you are signed in and try again.');
        }
      }
    })();

    return () => {
      disposed = true;
      window.removeEventListener('message', onWindowMessage);
      port?.close();
    };
  }, [open, frame, webUrl, filters]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => { if (!data.open) onClose(); }}>
      <DialogSurface className={styles.pickerSurface} aria-label="Choose files from SharePoint">
        <DialogBody className={styles.pickerBody}>
          <DialogTitle action={<Button appearance="subtle" aria-label="Close" icon={<Dismiss20Regular />} onClick={onClose} />}>
            Choose files from SharePoint
          </DialogTitle>
          <DialogContent className={styles.pickerContent}>
            {error && <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar>}
            {loading && !error && <Spinner className={styles.pickerSpinner} label="Loading SharePoint…" />}
            <iframe ref={setFrame} name="hello-copilot-file-picker" title="SharePoint file picker" className={styles.pickerFrame} />
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
