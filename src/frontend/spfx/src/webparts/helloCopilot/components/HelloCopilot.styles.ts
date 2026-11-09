import { createFocusOutlineStyle, makeStyles, shorthands, tokens, typographyStyles } from '@fluentui/react-components';

const reducedMotion = '@media (prefers-reduced-motion: reduce)';
const column = '760px';

const focusRing = createFocusOutlineStyle();

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
    backgroundColor: tokens.colorNeutralBackground2,
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
    gap: tokens.spacingHorizontalM,
    width: '100%',
    minHeight: '36px',
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
    fontWeight: tokens.fontWeightSemibold,
    '::before': {
      content: '""',
      position: 'absolute',
      left: '2px',
      top: '8px',
      bottom: '8px',
      width: '3px',
      borderRadius: tokens.borderRadiusCircular,
      backgroundColor: tokens.colorCompoundBrandForeground1
    },
    ':hover': {
      backgroundColor: tokens.colorSubtleBackgroundSelected,
      color: tokens.colorNeutralForeground1
    }
  },
  newChatIcon: {
    display: 'inline-flex',
    color: tokens.colorBrandForeground1
  },
  navIcon: {
    display: 'inline-flex',
    color: tokens.colorNeutralForeground2
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
  chipText: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
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
    overflowY: 'auto',
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL} 12vh`
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
    flexDirection: 'column',
    alignItems: 'flex-end',
    gap: tokens.spacingVerticalXXS
  },
  userSkill: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3
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
      position: 'relative',
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
  followUps: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM
  },
  followUp: {
    maxWidth: '100%',
    minWidth: 0,
    minHeight: '32px',
    whiteSpace: 'normal',
    overflowWrap: 'anywhere',
    textAlign: 'start',
    '@media (max-width: 600px)': { minHeight: '44px' }
  },
  activity: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXS
  },
  activityToggle: {
    position: 'relative',
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
    flexWrap: 'wrap',
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
  composerTop: {
    flexBasis: '100%',
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    maxHeight: '104px',
    overflowY: 'auto',
    scrollbarWidth: 'thin'
  },
  draftList: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS
  },
  tagContent: {
    maxWidth: '200px',
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  skillMenu: {
    maxWidth: '360px',
    maxHeight: '360px',
    overflowY: 'auto',
    overflowX: 'hidden'
  },
  skillDescription: {
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical',
    overflow: 'hidden',
    whiteSpace: 'normal'
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
    '::placeholder': { color: tokens.colorNeutralForeground4, opacity: 1 },
    '@media (forced-colors: active)': {
      '::placeholder': { color: 'GrayText' }
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
  },

  attachButton: {
    flexShrink: 0,
    alignSelf: 'flex-end',
    marginBottom: '4px',
    marginLeft: `calc(${tokens.spacingHorizontalS} * -1)`
  },
  composerLoading: {
    margin: '0 auto'
  },

  documentErrors: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalM
  },

  setPill: {
    flexShrink: 0,
    gap: tokens.spacingHorizontalXS,
    '&[aria-expanded="true"]': {
      ...shorthands.borderColor(tokens.colorCompoundBrandStroke),
      boxShadow: `0 0 0 1px ${tokens.colorCompoundBrandStroke} inset`
    }
  },
  iconStack: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '2px'
  },
  setSurface: {
    width: 'min(380px, calc(100vw - 32px))',
    padding: 0,
    display: 'flex',
    flexDirection: 'column'
  },
  setHeading: {
    ...typographyStyles.body1Strong,
    margin: 0,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL} ${tokens.spacingVerticalXS}`
  },
  setEmpty: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3,
    margin: 0,
    padding: `0 ${tokens.spacingHorizontalL} ${tokens.spacingVerticalS}`
  },
  setList: {
    listStyleType: 'none',
    margin: 0,
    padding: `0 0 ${tokens.spacingVerticalXS}`,
    maxHeight: '320px',
    overflowY: 'auto'
  },
  setItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`
  },
  setText: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0
  },
  setName: {
    ...typographyStyles.body1Strong,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  setMeta: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3
  },
  setFoot: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground2
  },
  eventDivider: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground3
  },
  inlineCitation: {
    position: 'relative',
    display: 'inline',
    font: 'inherit',
    color: tokens.colorBrandForegroundLink,
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.border('0'),
    borderRadius: tokens.borderRadiusSmall,
    padding: `0 ${tokens.spacingHorizontalXXS}`,
    cursor: 'pointer',
    ':hover': { textDecorationLine: 'underline' },
    ...focusRing
  },
  citationArea: {
    marginTop: tokens.spacingVerticalS,
    overflowWrap: 'anywhere'
  },
  citationList: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    '& button': { maxWidth: '100%', textAlign: 'start', overflowWrap: 'anywhere' }
  },
  citationEvidence: {
    marginTop: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    ...focusRing
  },
  citationTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalXS
  },
  citationQuote: {
    margin: `${tokens.spacingVerticalS} 0`,
    padding: 0,
    whiteSpace: 'pre-wrap',
    maxHeight: '280px',
    overflowY: 'auto'
  },
  retrievalAccordion: {
    marginTop: tokens.spacingVerticalXS
  },
  retrievalHeader: {
    '& button': {
      paddingLeft: 0,
      paddingRight: tokens.spacingHorizontalXS,
      minHeight: '28px',
      color: tokens.colorNeutralForeground3,
      ...typographyStyles.caption1Strong
    }
  },
  retrievalDetails: {
    margin: 0,
    color: tokens.colorNeutralForeground2,
    ...typographyStyles.caption1,
    '& dl': { margin: `${tokens.spacingVerticalXXS} 0` },
    '& dt': { fontWeight: tokens.fontWeightSemibold },
    '& dd': { marginLeft: 0, marginBottom: tokens.spacingVerticalXS }
  },

  pickerSurface: {
    width: 'min(1080px, 96vw)',
    maxWidth: 'min(1080px, 96vw)',
    padding: tokens.spacingHorizontalM
  },
  pickerBody: {
    display: 'flex',
    flexDirection: 'column'
  },
  pickerContent: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    height: 'min(680px, 75vh)',
    padding: 0
  },
  pickerSpinner: {
    position: 'absolute',
    inset: 0
  },
  pickerFrame: {
    flexGrow: 1,
    width: '100%',
    ...shorthands.border('0')
  }
});
