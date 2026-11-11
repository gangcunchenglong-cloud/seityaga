import type { Question } from '../../types';
import { renderChoice } from './choice';
import type { RenderApi, Rendered } from './common';

export function renderQuestionBody(q: Question, api: RenderApi): Rendered {
  switch (q.type) {
    case 'choice':
    case 'multi':
      return renderChoice(q, api);
    default:
      throw new Error(`未対応の形式: ${q.type}`);
  }
}
