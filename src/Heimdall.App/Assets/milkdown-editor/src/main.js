import { Crepe } from '@milkdown/crepe';
import '@milkdown/crepe/theme/common/style.css';
import '@milkdown/theme-nord/style.css';
import { editorViewCtx } from '@milkdown/kit/core';
import { Plugin, PluginKey } from '@milkdown/kit/prose/state';
import { Decoration, DecorationSet } from '@milkdown/kit/prose/view';
import { $prose, replaceAll } from '@milkdown/kit/utils';

import './styles.css';

const EDITOR_DEBOUNCE_MS = 200;
const WIKI_LINK_PATTERN = /\[\[([^[\]\r\n]+?)\]\]/g;

const appElement = document.getElementById('app');
const rootElement = document.createElement('div');
const hostElement = document.createElement('div');

rootElement.className = 'editor-shell';
hostElement.className = 'editor-host';
rootElement.append(hostElement);
appElement.append(rootElement);

let editor = null;
let editorView = null;
let isApplyingRemoteContent = false;
let isReadOnly = false;
let lastMarkdown = '';
let changeTimer = 0;
let isReady = false;

const pendingHostMessages = [];

const wikiLinkPlugin = $prose(() => {
  return new Plugin({
    key: new PluginKey('HEIMDALL_WIKI_LINKS'),
    props: {
      decorations(state) {
        const decorations = [];

        state.doc.descendants((node, pos, parent) => {
          if (!node.isText || !node.text || parent?.type.name === 'code_block') {
            return;
          }

          if (node.marks.some((mark) => mark.type.name === 'link' || mark.type.spec.code)) {
            return;
          }

          WIKI_LINK_PATTERN.lastIndex = 0;

          let match;
          while ((match = WIKI_LINK_PATTERN.exec(node.text)) !== null) {
            const rawMatch = match[0];
            const payload = match[1].trim();
            if (!payload) {
              continue;
            }

            decorations.push(
              Decoration.inline(
                pos + match.index,
                pos + match.index + rawMatch.length,
                {
                  class: 'wiki-link',
                  'data-wiki-link': payload,
                  title: payload,
                },
              ),
            );
          }
        });

        if (decorations.length === 0) {
          return DecorationSet.empty;
        }

        return DecorationSet.create(state.doc, decorations);
      },
      handleClick(_view, _position, event) {
        const target = event.target;
        if (!(target instanceof Element)) {
          return false;
        }

        const wikiLink = target.closest('[data-wiki-link]');
        if (!wikiLink) {
          return false;
        }

        const payload = wikiLink.getAttribute('data-wiki-link');
        if (!payload) {
          return false;
        }

        event.preventDefault();
        event.stopPropagation();

        postToHost({
          type: 'open-link',
          payload,
        });

        return true;
      },
    },
  });
});

function postToHost(message) {
  if (window.chrome?.webview?.postMessage) {
    window.chrome.webview.postMessage(message);
  }
}

function debounceChange(markdown) {
  lastMarkdown = markdown;
  window.clearTimeout(changeTimer);
  changeTimer = window.setTimeout(() => {
    postToHost({
      type: 'change',
      payload: {
        markdown,
        dirty: true,
      },
    });
  }, EDITOR_DEBOUNCE_MS);
}

function setTheme(theme) {
  const normalizedTheme = theme === 'dark' ? 'dark' : 'light';
  document.documentElement.dataset.theme = normalizedTheme;
}

function setReadOnly(value) {
  isReadOnly = Boolean(value);
  hostElement.classList.toggle('is-readonly', isReadOnly);
  editor?.setReadonly(isReadOnly);
}

async function setContent(markdown) {
  if (!editor?.editor) {
    return;
  }

  isApplyingRemoteContent = true;

  try {
    editor.editor.action(replaceAll(markdown ?? '', true));
    lastMarkdown = markdown ?? '';
  } finally {
    isApplyingRemoteContent = false;
  }
}

function insertText(text) {
  if (!text || !editorView) {
    return;
  }

  const { state } = editorView;
  const { from, to } = state.selection;
  const transaction = state.tr.insertText(text, from, to);
  editorView.dispatch(transaction.scrollIntoView());
  editorView.focus();
}

function focusEditor() {
  editorView?.focus();
}

function handleHostMessage(event) {
  const message = event?.data ?? event;
  if (!message || typeof message !== 'object') {
    return;
  }

  if (!isReady && message.type !== 'set-theme') {
    pendingHostMessages.push(message);
    return;
  }

  switch (message.type) {
    case 'set-content':
      void setContent(String(message.payload ?? ''));
      break;
    case 'set-theme':
      setTheme(message.payload);
      break;
    case 'set-readonly':
      setReadOnly(message.payload);
      break;
    case 'focus':
      focusEditor();
      break;
    case 'insert':
      insertText(String(message.payload ?? ''));
      break;
    default:
      break;
  }
}

function flushPendingMessages() {
  while (pendingHostMessages.length > 0) {
    handleHostMessage(pendingHostMessages.shift());
  }
}

async function createEditor() {
  if (window.chrome?.webview?.addEventListener) {
    window.chrome.webview.addEventListener('message', handleHostMessage);
  } else {
    window.addEventListener('message', handleHostMessage);
  }

  editor = new Crepe({
    root: hostElement,
    defaultValue: '',
  });

  editor.editor.use(wikiLinkPlugin);

  editor.on((listener) => {
    listener.mounted((ctx) => {
      editorView = ctx.get(editorViewCtx);
    });

    listener.markdownUpdated((_ctx, markdown) => {
        if (isApplyingRemoteContent) {
          return;
        }

        debounceChange(markdown);
    });
  });

  await editor.create();

  setTheme('light');
  setReadOnly(false);
  isReady = true;
  flushPendingMessages();

  postToHost({ type: 'ready' });
}

void createEditor();
