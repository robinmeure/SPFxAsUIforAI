import { makeStyles, shorthands, tokens, typographyStyles } from '@fluentui/react-components';

const reducedMotion = '@media (prefers-reduced-motion: reduce)';
const column = '760px';

const focusRing = {
  ':focus-visible': {
    outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`,
    outlineOffset: `calc(${tokens.strokeWidthThick} * -1)`
  }
};

const enter = {
  animationName: {
    from: { opacity: 0, transform: 'translateY(6px)' },
    to: { opacity: 1, transform: 'translateY(0)' }
  },
  animationDuration: tokens.durationNormal,
  animationTimingFunction: tokens.curveDecelerateMid,
  animationFillMode: 'both',
  [reducedMotion]: { animationName: 'none' }
};

export const useStyles = makeStyles({
  provider: {
    backgroundColor: 'transparent'
  },
  root: {
    position: 'relative',
    display: 'flex',
    height: 'clamp(480px, 80vh, 760px)',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    '& ::selection': {
      backgroundColor: tokens.colorBrandBackground2,
      color: tokens.colorNeutralForeground1
    }
  },
  rootPage: {
    borderRadius: tokens.borderRadiusXLarge,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2)
  },
  rootTeams: {
    height: '100vh'
  },

  sidebar: {
    display: 'flex',
    flexDirection: 'column',
    flexShrink: 0,
    width: '248px',
    maxWidth: '100%',
    height: '100%',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRight: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`
  },
  sidebarOverlay: {
    width: '248px',
    maxWidth: '100%',
    alignItems: 'stretch'
  },
  sidebarTop: {
    display: 'flex',
    alignItems: 'center',
    height: '48px',
    padding: `0 ${tokens.spacingHorizontalS}`
  },
  sidebarBody: {
    flexGrow: 1,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: `0 ${tokens.spacingHorizontalS} ${tokens.spacingVerticalM}`,
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`
  },
  sectionLabel: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3,
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalXS}`,
    margin: 0
  },
  chatList: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS
  },
  navItem: {
    position: 'relative',
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    width: '100%',
    minHeight: '32px',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    ...shorthands.border('0'),
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorSubtleBackground,
    color: tokens.colorNeutralForeground2,
    ...typographyStyles.body1,
    textAlign: 'start',
    cursor: 'pointer',
    transitionProperty: 'background-color, color',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':hover': {
      backgroundColor: tokens.colorSubtleBackgroundHover,
      color: tokens.colorNeutralForeground2Hover
    },
    ':active': { backgroundColor: tokens.colorSubtleBackgroundPressed },
    ...focusRing
  },
  navItemSelected: {
    backgroundColor: tokens.colorSubtleBackgroundSelected,
    color: tokens.colorNeutralForeground1,
    ':hover': {
      backgroundColor: tokens.colorSubtleBackgroundSelected,
      color: tokens.colorNeutralForeground1
    }
  },
  newChatIcon: {
    display: 'inline-flex',
    color: tokens.colorNeutralForeground1
  },
  chatRow: {
    position: 'relative',
    '& .hc-delete': { opacity: 0 },
    '&:hover .hc-delete': { opacity: 1 },
    '&:focus-within .hc-delete': { opacity: 1 },
    '@media (hover: none)': { '& .hc-delete': { opacity: 1 } }
  },
  chatTitle: {
    flexGrow: 1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    paddingRight: tokens.spacingHorizontalXXL
  },
  deleteButton: {
    position: 'absolute',
    right: tokens.spacingHorizontalXS,
    top: '50%',
    transform: 'translateY(-50%)',
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationFaster
  },
  deleteDescription: {
    overflowWrap: 'anywhere'
  },
  sidebarEmpty: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    margin: 0
  },

  main: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0
  },
  topBar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    height: '48px',
    flexShrink: 0,
    padding: `0 ${tokens.spacingHorizontalS}`
  },
  topTitle: {
    ...typographyStyles.body1Strong,
    flexGrow: 1,
    minWidth: 0,
    margin: 0,
    paddingLeft: tokens.spacingHorizontalS,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },

  welcome: {
    flexGrow: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalXXL,
    padding: `0 ${tokens.spacingHorizontalL} 12vh`
  },
  greeting: {
    margin: 0,
    ...typographyStyles.title3,
    color: tokens.colorNeutralForeground1,
    textAlign: 'center',
    textWrap: 'balance',
    ...enter
  },

  thread: {
    flexGrow: 1,
    overflowY: 'auto',
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    ...focusRing
  },
  threadInner: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXL,
    boxSizing: 'border-box',
    maxWidth: `calc(${column} + 2 * ${tokens.spacingHorizontalXL})`,
    margin: '0 auto',
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalXL} ${tokens.spacingVerticalXXL}`
  },
  row: {
    display: 'flex',
    ...enter
  },
  rowUser: {
    justifyContent: 'flex-end'
  },
  userBubble: {
    ...typographyStyles.body1,
    maxWidth: '80%',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusXLarge,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere'
  },
  assistant: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
    width: '100%'
  },
  assistantName: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...typographyStyles.caption1Strong,
    color: tokens.colorNeutralForeground2
  },
  avatar: {
    display: 'grid',
    placeItems: 'center',
    width: '24px',
    height: '24px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2
  },
  assistantText: {
    ...typographyStyles.body1,
    margin: 0,
    minWidth: 0,
    overflowWrap: 'anywhere',
    '& > :first-child': { marginTop: 0 },
    '& > :last-child': { marginBottom: 0 },
    '& p, & ul, & ol, & blockquote, & pre': {
      marginTop: tokens.spacingVerticalM,
      marginBottom: tokens.spacingVerticalM
    },
    '& ul, & ol': { paddingLeft: tokens.spacingHorizontalXXL },
    '& li + li': { marginTop: tokens.spacingVerticalXS },
    '& h3, & h4, & h5, & h6': {
      ...typographyStyles.subtitle2,
      marginTop: tokens.spacingVerticalL,
      marginBottom: tokens.spacingVerticalS
    },
    '& a': { textDecorationLine: 'underline' },
    '& code': {
      fontFamily: tokens.fontFamilyMonospace,
      backgroundColor: tokens.colorNeutralBackground3,
      borderRadius: tokens.borderRadiusSmall,
      padding: `0 ${tokens.spacingHorizontalXXS}`
    },
    '& pre': {
      maxWidth: '100%',
      boxSizing: 'border-box',
      overflowX: 'auto',
      padding: tokens.spacingHorizontalM,
      backgroundColor: tokens.colorNeutralBackground3,
      borderRadius: tokens.borderRadiusMedium,
      whiteSpace: 'pre',
      overflowWrap: 'normal',
      ...focusRing
    },
    '& pre code': { padding: 0 },
    '& blockquote': {
      marginLeft: 0,
      marginRight: 0,
      paddingLeft: tokens.spacingHorizontalL,
      color: tokens.colorNeutralForeground2
    }
  },
  activity: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXS
  },
  activityToggle: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXS, tokens.spacingVerticalXXS, tokens.spacingHorizontalXXS),
    ...shorthands.border('0'),
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground3,
    ...typographyStyles.caption1Strong,
    fontFamily: tokens.fontFamilyBase,
    cursor: 'pointer',
    ':hover': { color: tokens.colorNeutralForeground2, backgroundColor: tokens.colorSubtleBackgroundHover },
    ...focusRing
  },
  activityList: {
    listStyleType: 'none',
    margin: 0,
    paddingLeft: tokens.spacingHorizontalS,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    borderLeft: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`
  },
  activityStep: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3
  },
  notice: {
    width: '100%',
    maxWidth: '640px',
    margin: `0 auto ${tokens.spacingVerticalS}`,
    boxSizing: 'border-box'
  },
  actionButton: {
    flexShrink: 0,
    width: '40px',
    height: '40px',
    minWidth: '40px',
    padding: 0
  },
  thinking: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...typographyStyles.body1,
    color: tokens.colorNeutralForeground3
  },
  meta: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minHeight: '24px',
    marginLeft: `calc(${tokens.spacingHorizontalS} * -1)`
  },
  metaNote: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3,
    paddingLeft: tokens.spacingHorizontalS
  },

  composerDock: {
    flexShrink: 0,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalXL} ${tokens.spacingVerticalXL}`
  },
  composer: {
    display: 'flex',
    alignItems: 'flex-end',
    minHeight: '56px',
    gap: tokens.spacingHorizontalS,
    width: '100%',
    maxWidth: '640px',
    margin: '0 auto',
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalS} ${tokens.spacingHorizontalXL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: '28px',
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    boxShadow: tokens.shadow4,
    transitionProperty: 'border-color, box-shadow',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':hover': {
      ...shorthands.borderColor(tokens.colorNeutralStroke1Hover)
    },
    ':focus-within': {
      ...shorthands.borderColor(tokens.colorCompoundBrandStroke),
      boxShadow: `${tokens.shadow8}, 0 0 0 1px ${tokens.colorCompoundBrandStroke}`
    }
  },
  composerWide: {
    maxWidth: column
  },
  input: {
    flexGrow: 1,
    minWidth: 0,
    maxHeight: '160px',
    minHeight: '40px',
    boxSizing: 'border-box',
    padding: '10px 0',
    ...shorthands.border('0'),
    outlineStyle: 'none',
    resize: 'none',
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground1,
    caretColor: tokens.colorBrandForeground1,
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    scrollbarWidth: 'thin',
    '::placeholder': { color: tokens.colorNeutralForeground2, opacity: 1 },
    '@media (forced-colors: active)': {
      '::placeholder': { color: 'CanvasText' }
    }
  },
  visuallyHidden: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    margin: '-1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    whiteSpace: 'nowrap'
  }
});
