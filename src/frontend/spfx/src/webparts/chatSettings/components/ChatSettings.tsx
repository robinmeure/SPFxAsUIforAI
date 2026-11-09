import * as React from 'react';
import {
  Button,
  Field,
  FluentProvider,
  Input,
  MessageBar,
  MessageBarBody,
  Text,
  Theme,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import { IApiSettings, loadApiSettings, saveApiSettings, validateApiUrl } from '../../../common/apiSettings';
import { resolveEndpoints } from '../../../common/apiEndpoints';

const useStyles = makeStyles({
  provider: { minHeight: '100vh', backgroundColor: tokens.colorNeutralBackground2 },
  form: {
    display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL,
    maxWidth: '640px', padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalXXL}`
  },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS }
});

const EMPTY: IApiSettings = { apiUrl: '', apiResource: '', apiScope: '', companionBaseUrl: '' };
type Status = { intent: 'success' | 'error'; text: string } | undefined;

export const ChatSettings: React.FC<{ theme: Theme }> = ({ theme }) => {
  const styles = useStyles();
  const [values, setValues] = React.useState<IApiSettings>(() => ({ ...EMPTY, ...loadApiSettings() }));
  const [status, setStatus] = React.useState<Status>();
  const urlError = validateApiUrl(values.apiUrl);
  let companionError = validateApiUrl(values.companionBaseUrl ?? '');
  if (!urlError && !companionError && values.apiUrl.trim()) {
    try { resolveEndpoints(values.apiUrl, values.companionBaseUrl); }
    catch (error) { companionError = error instanceof Error ? error.message : 'Invalid endpoint configuration.'; }
  }

  const update = (key: keyof IApiSettings) => (_: unknown, data: { value: string }): void => {
    setValues(v => ({ ...v, [key]: data.value }));
    setStatus(undefined);
  };

  const persist = (next: IApiSettings, text: string): void => {
    try {
      saveApiSettings(next);
      setValues(next);
      setStatus({ intent: 'success', text });
    } catch {
      setStatus({ intent: 'error', text: 'The settings could not be saved because browser storage is unavailable.' });
    }
  };

  const onSubmit = (e: React.FormEvent): void => {
    e.preventDefault();
    if (!urlError && !companionError) persist(values, 'Saved. The Chat tab now uses these settings.');
  };

  return (
    <FluentProvider theme={theme} className={styles.provider}>
      <form className={styles.form} onSubmit={onSubmit}>
        <Text as="h1" size={600} weight="semibold">Chat settings</Text>
        <Text>
          These settings apply only to the Hello Copilot personal app, on this device. Leave a field empty to use the
          app&apos;s default. Teams channel tabs are configured from the tab&apos;s Settings instead.
        </Text>
        <Field label="API HTTPS URL" validationMessage={urlError}
          hint="For example https://<apim>.azure-api.net/chat/api/messages/stream">
          <Input type="url" value={values.apiUrl} onChange={update('apiUrl')} />
        </Field>
        <Field label="Companion base URL (optional)" validationMessage={companionError}
          hint="For custom stream paths, set the same-origin base for saved chats, documents and skills. Otherwise only streaming is available.">
          <Input type="url" value={values.companionBaseUrl ?? ''} onChange={update('companionBaseUrl')} />
        </Field>
        <Field label="API resource (Application ID URI)" hint="For example api://<client-id>">
          <Input value={values.apiResource} onChange={update('apiResource')} />
        </Field>
        <Field label="Delegated scope" hint="For example api://<client-id>/access_as_user">
          <Input value={values.apiScope} onChange={update('apiScope')} />
        </Field>
        {status && <MessageBar intent={status.intent}><MessageBarBody>{status.text}</MessageBarBody></MessageBar>}
        <div className={styles.actions}>
          <Button appearance="primary" type="submit" disabled={!!urlError || !!companionError}>Save</Button>
          <Button onClick={() => persist(EMPTY, 'Reset. The Chat tab now uses the app defaults.')}>
            Reset to defaults
          </Button>
        </div>
      </form>
    </FluentProvider>
  );
};
