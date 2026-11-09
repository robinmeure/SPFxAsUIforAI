---
name: extract-and-generate
description: Extract structured data (facts, fields, figures, tables, action items) from one or more of the user's documents and create a new Word, PDF or Excel document from it. Use when the user asks to summarise a document into a report, turn a document into a table or spreadsheet, fill a template or overview from source documents, or "make a document from" existing content.
---

# Extract data and generate a document

Follow these steps in order.

## 1. Identify the source documents

- If the user names or refers to a document, call `list_my_documents` and match it by file name.
- Use the application retrieval result, which is restricted to the thread's attached documents.
- If several documents match and the choice changes the result, ask the user which one to use.
  Do not guess between plausible candidates.
- Only use documents with status `ready`. If a document is still `processing`, tell the user to wait.

## 2. Check the retrieved evidence

The application supplies top matching passages, not the full text. Base extraction on those
passages and label the output as partial. For exhaustive extraction, explain the limitation
and ask the user to narrow the task to a section or specific fields.

## 3. Extract the data

- Decide the target structure before extracting: the fields or columns the user asked for, or, if
  unspecified, the structure from `references/output-guidelines.md` that best fits the request.
- Copy values exactly as written (names, amounts, dates, reference numbers). Do not round, translate
  or normalise unless the user asks to.
- Mark a value as `Not found in retrieved passages` when evidence is absent. Never invent values.
- Keep each value's passage number, file name, and available section/page for citations.

## 4. Confirm when needed

Ask one short clarifying question before creating the file when:

- the output format (Word, PDF or Excel) is not stated and cannot be inferred, or
- extracted values conflict between documents.

Otherwise proceed without asking. Default to **docx** for narrative output and **xlsx** for
tabular output with more than ~10 rows.

## 5. Create the document

Call `create_document` once:

- `docx` / `pdf`: put the content in `markdown`, set `sheets` to null. Start with a short summary
  paragraph, then sections with `##` headings, then tables. End with a "Sources" section listing
  the source file names.
- `xlsx`: put the data in `sheets`, set `markdown` to null. One header row in `columns`; every row
  must have the same number of cells as `columns`. Add a "Sources" sheet listing the source files.
- Use a descriptive `fileName` based on the content, for example `Overview contracts 2025`.

## 6. Reply

Reply briefly in the user's language: what was extracted, from which documents (with citations),
any values marked `Not found`, and the link to the created file.
