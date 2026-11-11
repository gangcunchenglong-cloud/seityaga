import './styles.css';
import { deviceTarget, isYoungGrade } from './config';
import { judgeGrade } from './logic/judge';
import { defaultRng } from './logic/random';
import { isCorrect } from './logic/score';
import { missingFields, selectQuestions } from './logic/select';
import { isOverTime, nextStep } from './logic/session';
import { BANK } from './questions';
import type { Profile, Run } from './state';
import type { Grade, Mode, QuestionResult } from './types';
import { detectDevice } from './ui/device';
import { createLayout, type Layout } from './ui/layout';
import { finalScreen } from './ui/screens/final';
import { gradeResultScreen } from './ui/screens/gradeResult';
import { questionScreen } from './ui/screens/question';
import { startScreen } from './ui/screens/start';
import { initSpeech } from './ui/speech';
import { resultExtras } from './ui/screens/resultExtras';

/** URL に ?mode=trial を付けると試作モード（draft の問題も出題） */
function readMode(): Mode {
  return new URLSearchParams(location.search).get('mode') === 'trial' ? 'trial' : 'production';
}

const mode = readMode();
const root = document.getElementById('app')!;
const layout: Layout = createLayout(root, mode, 'ICT活用能力テスト');
let timerId: number | undefined;
let run: Run | null = null;

const elapsed = () => (run ? performance.now() - run.startMs : 0);

/** 受験中にページを閉じようとしたら確認する（データは保存されないため） */
function guardUnload(e: BeforeUnloadEvent) {
  e.preventDefault();
  e.returnValue = '';
}

function showStart() {
  run = null;
  layout.setTimer(null);
  window.removeEventListener('beforeunload', guardUnload);
  layout.show(startScreen(BANK, mode, detectDevice(), begin));
}

function begin(profile: Profile) {
  run = {
    profile,
    startedAt: new Date(),
    startMs: performance.now(),
    results: [],
    usedIds: new Set(),
    rng: defaultRng,
  };
  window.addEventListener('beforeunload', guardUnload);
  clearInterval(timerId);
  layout.setTimer(0);
  timerId = window.setInterval(() => layout.setTimer(elapsed()), 1000);
  runGrade(profile.startGrade);
}

function runGrade(grade: Grade) {
  const r = run!;
  const device = deviceTarget(r.profile.device);
  const questions = selectQuestions(BANK, grade, device, r.profile.mode, r.rng, r.usedIds);
  questions.forEach((q) => r.usedIds.add(q.id));
  const results: QuestionResult[] = [];
  const gradeStart = performance.now();

  const ask = (i: number) => {
    if (i >= questions.length) return finishGrade();
    const q = questions[i];
    const screen = questionScreen({
      q,
      index: i,
      total: questions.length,
      device,
      rng: r.rng,
      overTime: isOverTime(elapsed()),
      onAnswer: (resp) => {
        results.push({ id: q.id, field: q.field, correct: isCorrect(q, resp) });
        ask(i + 1);
      },
    });
    layout.show(screen.el);
  };

  const finishGrade = () => {
    const sec = Math.round((performance.now() - gradeStart) / 1000);
    const gr = judgeGrade(grade, results, sec);
    r.results.push(gr);
    const step = nextStep(grade, gr.passed, elapsed(), (g) => missingFields(BANK, g, device, r.profile.mode).length === 0);
    layout.show(
      gradeResultScreen(gr, questions, step, isYoungGrade(grade), () => {
        if (step.kind === 'next') runGrade(step.grade);
        else {
          r.endReason = step.reason;
          showFinal();
        }
      }),
    );
  };

  ask(0);
}

function showFinal() {
  const r = run!;
  clearInterval(timerId);
  layout.setTimer(null);
  window.removeEventListener('beforeunload', guardUnload);
  layout.show(finalScreen(r, resultExtras(r, showStart)));
}

initSpeech();
showStart();
