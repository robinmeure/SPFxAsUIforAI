import { BaseClientSideWebPart } from '@microsoft/sp-webpart-base';
import * as React from 'react';
import * as ReactDom from 'react-dom';
import { Version } from '@microsoft/sp-core-library';
import { Theme, teamsDarkTheme, teamsHighContrastTheme, teamsLightTheme, webLightTheme } from '@fluentui/react-components';
import { ChatSettings } from './components/ChatSettings';

const teamsThemes: Record<string, Theme> = { dark: teamsDarkTheme, contrast: teamsHighContrastTheme };

/** The Settings tab of the Teams personal app, which has no property pane (supportedHosts is empty on purpose). */
export default class ChatSettingsWebPart extends BaseClientSideWebPart<Record<string, never>> {
  private _theme: Theme = webLightTheme;

  protected async onInit(): Promise<void> {
    await super.onInit();
    const teamsJs = this.context.sdks.microsoftTeams?.teamsJs;
    if (!teamsJs) return;
    const setTeamsTheme = (name?: string): void => {
      this._theme = (name && teamsThemes[name]) || teamsLightTheme;
      this.render();
    };
    try {
      setTeamsTheme((await teamsJs.app.getContext()).app.theme);
      teamsJs.app.registerOnThemeChangeHandler(setTeamsTheme);
    } catch {
      // Keep the default theme when the Teams context is unavailable.
    }
  }

  public render(): void {
    ReactDom.render(React.createElement(ChatSettings, { theme: this._theme }), this.domElement);
  }

  protected onDispose(): void {
    ReactDom.unmountComponentAtNode(this.domElement);
  }

  protected get dataVersion(): Version { return Version.parse('1.0'); }
}
