import { BaseClientSideWebPart } from '@microsoft/sp-webpart-base';
import * as React from 'react';
import * as ReactDom from 'react-dom';
import { Version } from '@microsoft/sp-core-library';
import { ThemeChangedEventArgs, ThemeProvider } from '@microsoft/sp-component-base';
import { IPropertyPaneConfiguration, PropertyPaneTextField } from '@microsoft/sp-property-pane';
import { Theme, teamsDarkTheme, teamsHighContrastTheme, teamsLightTheme, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { HelloCopilot } from './components/HelloCopilot';
import { API_SETTINGS_STORAGE_KEY, applyApiSettings } from '../../common/apiSettings';

export interface IHelloCopilotWebPartProps { apiUrl: string; apiScope: string; apiResource: string; companionBaseUrl?: string; }

const teamsThemes: Record<string, Theme> = { dark: teamsDarkTheme, contrast: teamsHighContrastTheme };

export default class HelloCopilotWebPart extends BaseClientSideWebPart<IHelloCopilotWebPartProps> {
  private _theme: Theme = webLightTheme;
  private _themeProvider: ThemeProvider | undefined;
  // A Teams personal app has no property pane; its Settings tab saves overrides to localStorage instead.
  private _isPersonalApp = false;

  protected async onInit(): Promise<void> {
    await super.onInit();
    const teamsJs = this.context.sdks.microsoftTeams?.teamsJs;
    if (teamsJs) {
      const setTeamsTheme = (name?: string): void => {
        this._theme = (name && teamsThemes[name]) || teamsLightTheme;
        this.render();
      };
      try {
        const teamsContext = await teamsJs.app.getContext();
        this._isPersonalApp = !teamsContext.team && !teamsContext.channel && !teamsContext.chat;
        if (this._isPersonalApp) window.addEventListener('storage', this._onStorage);
        setTeamsTheme(teamsContext.app.theme);
        teamsJs.app.registerOnThemeChangeHandler(setTeamsTheme);
        return;
      } catch {
        // Fall back to the SharePoint theme when the Teams context is unavailable.
      }
    }
    this._themeProvider = this.context.serviceScope.consume(ThemeProvider.serviceKey);
    this._applySharePointTheme(this._themeProvider.tryGetThemeV2(), this._themeProvider.tryGetTheme()?.isInverted);
    this._themeProvider.themeChangedEvent.add(this, this._onThemeChanged);
  }

  public render(): void {
    const properties = this._isPersonalApp ? applyApiSettings(this.properties) : this.properties;
    ReactDom.render(React.createElement(HelloCopilot, {
      context: this.context, theme: this._theme, isTeams: !!this.context.sdks.microsoftTeams, ...properties
    }), this.domElement);
  }

  protected onDispose(): void {
    window.removeEventListener('storage', this._onStorage);
    this._themeProvider?.themeChangedEvent.remove(this, this._onThemeChanged);
    ReactDom.unmountComponentAtNode(this.domElement);
  }

  protected get dataVersion(): Version { return Version.parse('1.0'); }

  protected getPropertyPaneConfiguration(): IPropertyPaneConfiguration {
    return {
      pages: [{ header: { description: 'Configuration' }, groups: [{ groupName: 'Mock API', groupFields: [
        PropertyPaneTextField('apiUrl', { label: 'API HTTPS URL' }),
        PropertyPaneTextField('companionBaseUrl', { label: 'Companion base URL (optional)', description: 'For custom stream paths: base URL for saved chats, documents and skills, on the same origin.' }),
        PropertyPaneTextField('apiResource', { label: 'API resource (Application ID URI)' }),
        PropertyPaneTextField('apiScope', { label: 'Delegated scope' })
      ] }] }]
    };
  }

  private _onStorage = (e: StorageEvent): void => {
    if (e.key === API_SETTINGS_STORAGE_KEY || e.key === null) this.render();
  };

  private _onThemeChanged(args: ThemeChangedEventArgs): void {
    this._applySharePointTheme(args.themeV2, args.theme?.isInverted);
    this.render();
  }

  private _applySharePointTheme(themeV2: Theme | undefined, isInverted: boolean | undefined): void {
    this._theme = themeV2 ?? (isInverted ? webDarkTheme : webLightTheme);
  }
}
