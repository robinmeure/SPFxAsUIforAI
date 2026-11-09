import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Badge,
  Button,
  Card,
  Link
} from '@fluentui/react-components';
import { PassageCitation, RetrievalDetails } from './api';
import { useStyles } from './HelloCopilot.styles';
import { citationLocation, safeSourceUrl, sourceLabel } from './citationHelpers';

interface Props {
  citations: PassageCitation[];
  selected?: number;
  onSelect: (number: number | undefined) => void;
  retrieval?: RetrievalDetails;
}

export const CitationEvidence: React.FC<Props> = ({ citations, selected, onSelect, retrieval }) => {
  const styles = useStyles();
  const evidenceRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    if (selected !== undefined) evidenceRef.current?.focus();
  }, [selected]);
  const current = citations.find(citation => citation.number === selected);
  const url = safeSourceUrl(current?.sourceUrl);
  return (
    <div className={styles.citationArea}>
      {citations.length > 0 && (
        <>
          <div className={styles.citationList} aria-label="Source passages">
            {citations.map(citation => (
              <Button key={citation.number} size="small" appearance={selected === citation.number ? 'primary' : 'subtle'}
                aria-expanded={selected === citation.number}
                onClick={() => onSelect(selected === citation.number ? undefined : citation.number)}>
                [{citation.number}] {citation.title}
              </Button>
            ))}
          </div>
          {current && (
            <Card ref={evidenceRef} tabIndex={-1} appearance="filled-alternative" size="small" className={styles.citationEvidence}
              role="region" aria-label={`Citation ${current.number}: ${current.title}`}>
              <div className={styles.citationTitle}>
                <Badge appearance="tint">{sourceLabel(current.sourceType)}</Badge>
                <strong>{current.title}</strong>
              </div>
              <div>{citationLocation(current)}</div>
              <p className={styles.metaNote}>{current.retrievalMethod === 'reuse'
                ? 'Reused from an earlier answer; originally retrieved by hybrid keyword + vector search. Supporting excerpt:'
                : 'Retrieved by hybrid keyword + vector search. Supporting excerpt:'}</p>
              <blockquote className={styles.citationQuote}>{current.quote}</blockquote>
              {url ? <Link href={url} target="_blank" rel="noopener noreferrer">Open source document</Link>
                : <span className={styles.metaNote}>No source link is available. The indexed excerpt is shown above.</span>}
            </Card>
          )}
        </>
      )}
      {retrieval?.method === 'none' && retrieval.scope !== 'conversation' && retrieval.notice && <p className={styles.metaNote}>{retrieval.notice}</p>}
      {retrieval && retrieval.method !== 'none' && (
        <Accordion collapsible className={styles.retrievalAccordion}>
          <AccordionItem value="retrieval">
            <AccordionHeader size="small" className={styles.retrievalHeader}>
              {retrieval.method === 'reuse' ? 'Evidence details' : 'Search details'} · {retrieval.passageCount} {retrieval.passageCount === 1 ? 'passage' : 'passages'} {retrieval.method === 'reuse' ? 'reused' : 'retrieved'}
            </AccordionHeader>
            <AccordionPanel className={styles.retrievalDetails}>
              <dl>
                <dt>Query</dt><dd>{retrieval.query}</dd>
                <dt>Scope</dt><dd>{retrieval.scope === 'thread' ? 'Attached documents' : retrieval.scope}
                  {retrieval.section ? ` · ${retrieval.section}` : ''}{retrieval.pageNumber ? ` · Page ${retrieval.pageNumber}` : ''}</dd>
              </dl>
              {retrieval.notice && <p>{retrieval.notice}</p>}
            </AccordionPanel>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
};
