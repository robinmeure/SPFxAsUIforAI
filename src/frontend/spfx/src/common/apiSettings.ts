export interface IApiSettings { apiUrl: string; apiResource: string; apiScope: string; companionBaseUrl?: string; }

// The personal app's Chat and Settings tabs load from the same SharePoint origin, so they share localStorage.
export const API_SETTINGS_STORAGE_KEY = 'chatbot.personalApp.apiSettings.v1';
const KEYS: (keyof IApiSettings)[] = ['apiUrl', 'apiResource', 'apiScope', 'companionBaseUrl'];

const clean = (value: Partial<Record<keyof IApiSettings, unknown>> | null | undefined): Partial<IApiSettings> => {
  const result: Partial<IApiSettings> = {};
  for (const key of KEYS) {
    const v = value?.[key];
    if (typeof v === 'string' && v.trim()) result[key] = v.trim();
  }
  return result;
};

/** Returns the saved overrides; empty when nothing is saved or storage is unavailable. */
export const loadApiSettings = (): Partial<IApiSettings> => {
  try {
    const raw = window.localStorage.getItem(API_SETTINGS_STORAGE_KEY);
    return raw ? clean(JSON.parse(raw)) : {};
  } catch {
    return {};
  }
};

/** Saves the non-empty values; saving only empty values removes the overrides. Throws when storage is unavailable. */
export const saveApiSettings = (settings: Partial<IApiSettings>): void => {
  const values = clean(settings);
  if (Object.keys(values).length) window.localStorage.setItem(API_SETTINGS_STORAGE_KEY, JSON.stringify(values));
  else window.localStorage.removeItem(API_SETTINGS_STORAGE_KEY);
};

export const applyApiSettings = <T extends IApiSettings>(properties: T): T => ({ ...properties, ...loadApiSettings() });

export const validateApiUrl = (value: string): string | undefined => {
  if (!value.trim()) return undefined;
  try {
    const url = new URL(value.trim());
    if (url.protocol === 'https:' || (url.protocol === 'http:' && url.hostname === 'localhost')) return undefined;
    return 'Use an https:// URL (http:// is only allowed for localhost).';
  } catch {
    return 'Enter a full URL, for example https://<apim>.azure-api.net/chat/api/messages/stream.';
  }
};
