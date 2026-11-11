import type { Question } from '../../types';
import { renderChoice } from './choice';
import type { RenderApi, Rendered } from './common';
import { renderInput } from './input';
import { renderMatch } from './match';
import { renderOrder } from './order';
import { renderUiTask } from './uitask';

/** 問題の形式ごとに部品を切り替える */
export function renderQuestionBody(q: Question, api: RenderApi): Rendered {
  switch (q.type) {
    case 'choice':
    case 'multi':
      return renderChoice(q, api);
    case 'order':
      return renderOrder(q, api);
    case 'match':
      return renderMatch(q, api);
    case 'input':
      return renderInput(q, api);
    case 'ui-task':
      return renderUiTask(q, api);
  }
}
