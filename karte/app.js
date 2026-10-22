'use strict';

/* =========================================================
 * 電子カルテシステム
 * データはブラウザの localStorage に保存されます。
 * ========================================================= */

const STORAGE_KEY = 'karte-data-v1';

// 異常値の判定基準(一般的な成人の目安)
const VITAL_RULES = {
  bpHigh: { label: '血圧(上)', unit: 'mmHg', isAbnormal: (v) => v >= 140 || v < 90 },
  bpLow:  { label: '血圧(下)', unit: 'mmHg', isAbnormal: (v) => v >= 90 || v < 50 },
  pulse:  { label: '脈拍',     unit: '回/分', isAbnormal: (v) => v > 100 || v < 50 },
  temp:   { label: '体温',     unit: '℃',    isAbnormal: (v) => v >= 37.5 || v < 35 },
  spo2:   { label: 'SpO2',     unit: '%',    isAbnormal: (v) => v < 95 },
  weight: { label: '体重',     unit: 'kg',   isAbnormal: () => false },
};

let state = { patients: [], records: [], nextPatientNo: 1 };
let selectedPatientId = null;
let editingPatientId = null;
let editingRecordId = null;
let recordFilter = { keyword: '', department: '' };

/* ---------------- ユーティリティ ---------------- */

const $ = (sel) => document.querySelector(sel);

// XSS対策:画面に表示する文字列は必ずエスケープする
function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function generateId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

function calcAge(birthDate) {
  if (!birthDate) return '';
  const birth = new Date(birthDate);
  const today = new Date();
  let age = today.getFullYear() - birth.getFullYear();
  const m = today.getMonth() - birth.getMonth();
  if (m < 0 || (m === 0 && today.getDate() < birth.getDate())) age--;
  return age;
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleDateString('ja-JP', { year: 'numeric', month: '2-digit', day: '2-digit' });
}

function formatDateTime(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleString('ja-JP', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
  });
}

// datetime-local 入力欄用の "YYYY-MM-DDTHH:MM" 形式
function toLocalInputValue(date) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function parseAllergies(text) {
  return text.split(/[,、,]/).map((s) => s.trim()).filter(Boolean);
}

/* ---------------- 保存・読み込み ---------------- */

function saveState() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

function loadState() {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (raw) {
    try {
      state = JSON.parse(raw);
      return;
    } catch (e) {
      console.error('保存データの読み込みに失敗しました', e);
    }
  }
  seedSampleData();
}

function seedSampleData() {
  const p1 = createPatientObject({
    name: '山田 太郎', kana: 'ヤマダ タロウ', birthDate: '1965-04-12', sex: '男性',
    bloodType: 'A+', phone: '090-1111-2222', address: '東京都千代田区1-1-1',
    insurance: '06123456', emergencyContact: '山田 花子(妻) 090-3333-4444',
    allergies: ['ペニシリン'], history: '高血圧(2015年〜)、脂質異常症',
  });
  const p2 = createPatientObject({
    name: '鈴木 花子', kana: 'スズキ ハナコ', birthDate: '1990-09-30', sex: '女性',
    bloodType: 'O+', phone: '080-5555-6666', address: '神奈川県横浜市2-2-2',
    insurance: '01987654', emergencyContact: '', allergies: ['卵', 'ロキソプロフェン'],
    history: '気管支喘息(小児期)',
  });
  state.patients.push(p1, p2);

  state.records.push(
    {
      id: generateId(), patientId: p1.id, date: '2026-09-01T10:30', department: '循環器内科',
      doctor: '佐藤 医師', diagnosis: '本態性高血圧症',
      vitals: { bpHigh: 152, bpLow: 94, pulse: 78, temp: 36.4, spo2: 98, weight: 72.5 },
      s: '最近、朝に頭が重い感じがする。', o: '心音整、雑音なし。下腿浮腫なし。',
      a: '血圧コントロール不良。', p: '降圧薬を増量し、2週間後に再診。家庭血圧の記録を指導。',
      prescriptions: [{ name: 'アムロジピン錠5mg', dose: '1回1錠', usage: '1日1回 朝食後', days: 14 }],
      createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    },
    {
      id: generateId(), patientId: p1.id, date: '2026-09-15T11:00', department: '循環器内科',
      doctor: '佐藤 医師', diagnosis: '本態性高血圧症',
      vitals: { bpHigh: 134, bpLow: 84, pulse: 72, temp: 36.5, spo2: 98, weight: 72.0 },
      s: '頭重感は軽減した。', o: '家庭血圧 130/80 前後で推移。',
      a: '血圧改善傾向。', p: '現処方継続。1か月後再診。',
      prescriptions: [{ name: 'アムロジピン錠5mg', dose: '1回1錠', usage: '1日1回 朝食後', days: 28 }],
      createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    },
    {
      id: generateId(), patientId: p2.id, date: '2026-10-10T15:20', department: '内科',
      doctor: '高橋 医師', diagnosis: '急性上気道炎',
      vitals: { bpHigh: 112, bpLow: 70, pulse: 92, temp: 37.8, spo2: 97, weight: 51.2 },
      s: '2日前から咽頭痛と発熱。咳は軽度。', o: '咽頭発赤あり。肺音清。',
      a: 'ウイルス性上気道炎の疑い。NSAIDsアレルギーあり。', p: 'アセトアミノフェンで対症療法。3日後に改善なければ再診。',
      prescriptions: [{ name: 'カロナール錠200mg', dose: '1回2錠', usage: '発熱時 頓用', days: 5 }],
      createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    },
  );
  saveState();
}

function createPatientObject(data) {
  const now = new Date().toISOString();
  const no = state.nextPatientNo++;
  return {
    id: 'P' + String(no).padStart(6, '0'),
    ...data,
    createdAt: now,
    updatedAt: now,
  };
}

/* ---------------- 患者一覧 ---------------- */

function renderPatientList() {
  const keyword = $('#patient-search').value.trim().toLowerCase();
  const list = $('#patient-list');

  const patients = state.patients
    .filter((p) => !keyword ||
      p.id.toLowerCase().includes(keyword) ||
      p.name.toLowerCase().includes(keyword) ||
      p.kana.toLowerCase().includes(keyword))
    .sort((a, b) => a.kana.localeCompare(b.kana, 'ja'));

  if (patients.length === 0) {
    list.innerHTML = '<li class="no-result">該当する患者がいません</li>';
    return;
  }

  list.innerHTML = patients.map((p) => `
    <li data-id="${escapeHtml(p.id)}" class="${p.id === selectedPatientId ? 'selected' : ''}">
      <div class="pid">${escapeHtml(p.id)}</div>
      <div class="pname">${escapeHtml(p.name)}</div>
      <div class="pmeta">${escapeHtml(p.kana)} / ${calcAge(p.birthDate)}歳 / ${escapeHtml(p.sex)}
        ${p.allergies && p.allergies.length ? ' / ⚠アレルギー' : ''}</div>
    </li>`).join('');
}

/* ---------------- 患者詳細 ---------------- */

function renderMain() {
  const main = $('#main');
  const patient = state.patients.find((p) => p.id === selectedPatientId);
  if (!patient) {
    main.innerHTML = '<div class="empty-state"><p>左の一覧から患者を選択するか、新規患者を登録してください。</p></div>';
    return;
  }

  const allRecords = getPatientRecords(patient.id);
  const records = allRecords.filter(matchesRecordFilter);
  const departments = [...new Set(allRecords.map((r) => r.department))];

  main.innerHTML = `
    ${patient.allergies && patient.allergies.length
      ? `<div class="alert alert-danger">⚠ アレルギー:${patient.allergies.map(escapeHtml).join('、')}</div>`
      : ''}

    <section class="panel">
      <div class="panel-header">
        <h2 class="patient-title">${escapeHtml(patient.name)}
          <small>${escapeHtml(patient.kana)}|${escapeHtml(patient.id)}</small></h2>
        <div class="btn-group">
          <button class="btn" data-action="print">印刷</button>
          <button class="btn" data-action="edit-patient">患者情報を編集</button>
          <button class="btn btn-danger" data-action="delete-patient">患者を削除</button>
        </div>
      </div>
      <dl class="info-grid">
        ${infoItem('生年月日', `${formatDate(patient.birthDate)}(${calcAge(patient.birthDate)}歳)`)}
        ${infoItem('性別', patient.sex)}
        ${infoItem('血液型', patient.bloodType || '不明')}
        ${infoItem('電話番号', patient.phone)}
        ${infoItem('保険者番号', patient.insurance)}
        ${infoItem('緊急連絡先', patient.emergencyContact)}
        ${infoItem('住所', patient.address, true)}
        ${infoItem('既往歴', patient.history, true)}
      </dl>
    </section>

    <section class="panel">
      <div class="panel-header">
        <h2>フリーメモ</h2>
        <span id="memo-status" class="record-meta">${patient.memoUpdatedAt ? `最終保存 ${formatDateTime(patient.memoUpdatedAt)}` : '入力すると自動で保存されます'}</span>
      </div>
      <textarea id="patient-memo" class="memo" rows="5"
        placeholder="この患者について自由に書き留められます(生活背景、注意事項、次回確認したいことなど)">${escapeHtml(patient.memo)}</textarea>
      <div class="memo-print">${escapeHtml(patient.memo || '—')}</div>
    </section>

    ${renderVitalsTrend(allRecords)}

    <section class="panel">
      <div class="panel-header">
        <h2>診療記録(${records.length}件${records.length !== allRecords.length ? ` / 全${allRecords.length}件` : ''})</h2>
        <button class="btn btn-primary" data-action="new-record">+ 新規記録</button>
      </div>
      <div class="filter-bar">
        <input type="search" id="record-keyword" placeholder="記録内を検索(傷病名・SOAP・薬剤など)"
          value="${escapeHtml(recordFilter.keyword)}">
        <select id="record-department">
          <option value="">全診療科</option>
          ${departments.map((d) => `<option ${d === recordFilter.department ? 'selected' : ''}>${escapeHtml(d)}</option>`).join('')}
        </select>
      </div>
      ${records.length ? records.map(renderRecord).join('') : '<p class="record-meta">診療記録はありません。</p>'}
    </section>
  `;
}

function infoItem(label, value, wide = false) {
  return `<div class="${wide ? 'wide' : ''}"><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value || '—')}</dd></div>`;
}

function getPatientRecords(patientId) {
  return state.records
    .filter((r) => r.patientId === patientId)
    .sort((a, b) => b.date.localeCompare(a.date)); // 新しい順
}

function matchesRecordFilter(record) {
  if (recordFilter.department && record.department !== recordFilter.department) return false;
  const kw = recordFilter.keyword.trim().toLowerCase();
  if (!kw) return true;
  const haystack = [
    record.diagnosis, record.doctor, record.department, record.s, record.o, record.a, record.p, record.freeText,
    ...(record.prescriptions || []).map((rx) => rx.name),
  ].join(' ').toLowerCase();
  return haystack.includes(kw);
}

function renderRecord(r) {
  const v = r.vitals || {};
  const vitalParts = [];
  if (v.bpHigh || v.bpLow) {
    const abnormal = (v.bpHigh && VITAL_RULES.bpHigh.isAbnormal(v.bpHigh)) ||
      (v.bpLow && VITAL_RULES.bpLow.isAbnormal(v.bpLow));
    vitalParts.push(`<span class="${abnormal ? 'abnormal' : ''}">血圧 ${escapeHtml(v.bpHigh ?? '—')}/${escapeHtml(v.bpLow ?? '—')} mmHg</span>`);
  }
  ['pulse', 'temp', 'spo2', 'weight'].forEach((key) => {
    if (v[key] === undefined || v[key] === null || v[key] === '') return;
    const rule = VITAL_RULES[key];
    const abnormal = rule.isAbnormal(Number(v[key]));
    vitalParts.push(`<span class="${abnormal ? 'abnormal' : ''}">${rule.label} ${escapeHtml(v[key])}${rule.unit}</span>`);
  });

  const soap = [['S', r.s], ['O', r.o], ['A', r.a], ['P', r.p]]
    .filter(([, text]) => text)
    .map(([key, text]) => `<dt>${key}</dt><dd>${escapeHtml(text)}</dd>`)
    .join('');

  const rx = (r.prescriptions || []).map((item) =>
    `<li>${escapeHtml(item.name)} ${escapeHtml(item.dose)} ${escapeHtml(item.usage)}${item.days ? ` ${escapeHtml(item.days)}日分` : ''}</li>`,
  ).join('');

  return `
    <article class="record">
      <div class="record-head">
        <div>
          <div class="record-date">${formatDateTime(r.date)}</div>
          <div class="record-meta"><span class="tag">${escapeHtml(r.department)}</span>担当:${escapeHtml(r.doctor)}
            ${r.updatedAt && r.updatedAt !== r.createdAt ? `(最終更新 ${formatDateTime(r.updatedAt)})` : ''}</div>
        </div>
        <div class="btn-group">
          <button class="btn btn-small" data-action="edit-record" data-id="${escapeHtml(r.id)}">編集</button>
          <button class="btn btn-small btn-danger" data-action="delete-record" data-id="${escapeHtml(r.id)}">削除</button>
        </div>
      </div>
      ${r.diagnosis ? `<div class="record-diagnosis"><strong>傷病名:</strong>${escapeHtml(r.diagnosis)}</div>` : ''}
      ${vitalParts.length ? `<div class="vitals-line">${vitalParts.join('')}</div>` : ''}
      ${soap ? `<dl class="soap">${soap}</dl>` : ''}
      ${r.freeText ? `<div class="free-text"><strong>フリー記載:</strong><div>${escapeHtml(r.freeText)}</div></div>` : ''}
      ${rx ? `<div><strong>処方:</strong><ul class="rx-list">${rx}</ul></div>` : ''}
    </article>`;
}

function renderVitalsTrend(records) {
  const withVitals = records
    .filter((r) => r.vitals && Object.values(r.vitals).some((v) => v !== null && v !== ''))
    .slice(0, 10)
    .reverse(); // 古い順に並べて推移を見やすくする
  if (withVitals.length === 0) return '';

  const cell = (key, value) => {
    if (value === undefined || value === null || value === '') return '<td>—</td>';
    const abnormal = VITAL_RULES[key].isAbnormal(Number(value));
    return `<td class="${abnormal ? 'abnormal' : ''}">${escapeHtml(value)}</td>`;
  };

  return `
    <section class="panel">
      <h2>バイタル推移(直近${withVitals.length}回)</h2>
      <div style="overflow-x:auto">
        <table class="vitals-table">
          <thead><tr><th>日付</th>${Object.values(VITAL_RULES).map((r) => `<th>${r.label}<br><small>${r.unit}</small></th>`).join('')}</tr></thead>
          <tbody>
            ${withVitals.map((r) => `<tr><td>${formatDate(r.date)}</td>${Object.keys(VITAL_RULES).map((k) => cell(k, r.vitals[k])).join('')}</tr>`).join('')}
          </tbody>
        </table>
      </div>
    </section>`;
}

/* ---------------- 患者の登録・編集・削除 ---------------- */

function openPatientDialog(patient = null) {
  const form = $('#patient-form');
  form.reset();
  editingPatientId = patient ? patient.id : null;
  $('#patient-dialog-title').textContent = patient ? '患者情報の編集' : '新規患者登録';

  if (patient) {
    ['name', 'kana', 'birthDate', 'sex', 'bloodType', 'phone', 'address', 'insurance', 'emergencyContact', 'history']
      .forEach((key) => { form.elements[key].value = patient[key] || ''; });
    form.elements.allergies.value = (patient.allergies || []).join(', ');
  }
  $('#patient-dialog').showModal();
}

function handlePatientSubmit(event) {
  event.preventDefault();
  const form = event.target;
  const data = {
    name: form.elements.name.value.trim(),
    kana: form.elements.kana.value.trim(),
    birthDate: form.elements.birthDate.value,
    sex: form.elements.sex.value,
    bloodType: form.elements.bloodType.value,
    phone: form.elements.phone.value.trim(),
    address: form.elements.address.value.trim(),
    insurance: form.elements.insurance.value.trim(),
    emergencyContact: form.elements.emergencyContact.value.trim(),
    allergies: parseAllergies(form.elements.allergies.value),
    history: form.elements.history.value.trim(),
  };

  if (new Date(data.birthDate) > new Date()) {
    alert('生年月日が未来の日付になっています。');
    return;
  }

  if (editingPatientId) {
    const patient = state.patients.find((p) => p.id === editingPatientId);
    Object.assign(patient, data, { updatedAt: new Date().toISOString() });
  } else {
    const patient = createPatientObject(data);
    state.patients.push(patient);
    selectedPatientId = patient.id;
  }

  saveState();
  $('#patient-dialog').close();
  renderPatientList();
  renderMain();
}

function deletePatient(patientId) {
  const patient = state.patients.find((p) => p.id === patientId);
  const recordCount = state.records.filter((r) => r.patientId === patientId).length;
  const ok = confirm(`患者「${patient.name}」を削除しますか?\n関連する診療記録 ${recordCount} 件も削除されます。この操作は元に戻せません。`);
  if (!ok) return;

  state.patients = state.patients.filter((p) => p.id !== patientId);
  state.records = state.records.filter((r) => r.patientId !== patientId);
  selectedPatientId = null;
  saveState();
  renderPatientList();
  renderMain();
}

/* ---------------- 診療記録の作成・編集・削除 ---------------- */

function addRxRow(rx = {}) {
  const row = $('#rx-row-template').content.firstElementChild.cloneNode(true);
  row.querySelector('[name="rxName"]').value = rx.name || '';
  row.querySelector('[name="rxDose"]').value = rx.dose || '';
  row.querySelector('[name="rxUsage"]').value = rx.usage || '';
  row.querySelector('[name="rxDays"]').value = rx.days || '';
  $('#rx-rows').appendChild(row);
}

function openRecordDialog(record = null) {
  const form = $('#record-form');
  form.reset();
  $('#rx-rows').innerHTML = '';
  editingRecordId = record ? record.id : null;
  $('#record-dialog-title').textContent = record ? '診療記録の編集' : '診療記録の作成';

  if (record) {
    ['date', 'department', 'doctor', 'diagnosis', 's', 'o', 'a', 'p', 'freeText']
      .forEach((key) => { form.elements[key].value = record[key] || ''; });
    Object.keys(VITAL_RULES).forEach((key) => {
      form.elements[key].value = record.vitals?.[key] ?? '';
    });
    (record.prescriptions || []).forEach(addRxRow);
  } else {
    form.elements.date.value = toLocalInputValue(new Date());
    // 前回の診療科・担当医を初期値にして入力の手間を減らす
    const last = getPatientRecords(selectedPatientId)[0];
    if (last) {
      form.elements.department.value = last.department;
      form.elements.doctor.value = last.doctor;
    }
  }
  if ($('#rx-rows').children.length === 0) addRxRow();
  checkRxAllergy();
  $('#record-dialog').showModal();
}

// 処方薬の名前に患者のアレルギー物質が含まれていれば警告を表示する
function checkRxAllergy() {
  const patient = state.patients.find((p) => p.id === selectedPatientId);
  const warning = $('#rx-allergy-warning');
  const allergies = patient?.allergies || [];
  const names = [...document.querySelectorAll('#rx-rows [name="rxName"]')].map((i) => i.value.trim()).filter(Boolean);

  const hits = [];
  names.forEach((name) => {
    allergies.forEach((allergy) => {
      if (name.toLowerCase().includes(allergy.toLowerCase())) hits.push(`${name}(${allergy})`);
    });
  });

  warning.hidden = hits.length === 0;
  warning.textContent = hits.length ? `⚠ アレルギー登録のある薬剤が含まれています:${hits.join('、')}` : '';
  return hits;
}

function handleRecordSubmit(event) {
  event.preventDefault();
  const form = event.target;

  const hits = checkRxAllergy();
  if (hits.length && !confirm(`アレルギー登録のある薬剤が含まれています。\n${hits.join('\n')}\n\nこのまま保存しますか?`)) {
    return;
  }

  const vitals = {};
  Object.keys(VITAL_RULES).forEach((key) => {
    const value = form.elements[key].value;
    vitals[key] = value === '' ? null : Number(value);
  });

  const prescriptions = [...document.querySelectorAll('#rx-rows tr')]
    .map((row) => ({
      name: row.querySelector('[name="rxName"]').value.trim(),
      dose: row.querySelector('[name="rxDose"]').value.trim(),
      usage: row.querySelector('[name="rxUsage"]').value.trim(),
      days: row.querySelector('[name="rxDays"]').value ? Number(row.querySelector('[name="rxDays"]').value) : null,
    }))
    .filter((rx) => rx.name);

  const data = {
    date: form.elements.date.value,
    department: form.elements.department.value,
    doctor: form.elements.doctor.value.trim(),
    diagnosis: form.elements.diagnosis.value.trim(),
    s: form.elements.s.value.trim(),
    o: form.elements.o.value.trim(),
    a: form.elements.a.value.trim(),
    p: form.elements.p.value.trim(),
    freeText: form.elements.freeText.value.trim(),
    vitals,
    prescriptions,
  };

  const now = new Date().toISOString();
  if (editingRecordId) {
    const record = state.records.find((r) => r.id === editingRecordId);
    Object.assign(record, data, { updatedAt: now });
  } else {
    state.records.push({ id: generateId(), patientId: selectedPatientId, ...data, createdAt: now, updatedAt: now });
  }

  saveState();
  $('#record-dialog').close();
  renderMain();
}

function deleteRecord(recordId) {
  const record = state.records.find((r) => r.id === recordId);
  if (!confirm(`${formatDateTime(record.date)} の診療記録を削除しますか?\nこの操作は元に戻せません。`)) return;
  state.records = state.records.filter((r) => r.id !== recordId);
  saveState();
  renderMain();
}

/* ---------------- 書き出し・読み込み ---------------- */

function exportData() {
  const blob = new Blob([JSON.stringify(state, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `karte-backup-${toLocalInputValue(new Date()).replace(/[:T]/g, '-')}.json`;
  a.click();
  URL.revokeObjectURL(url);
}

function importData(file) {
  const reader = new FileReader();
  reader.onload = () => {
    try {
      const data = JSON.parse(reader.result);
      if (!Array.isArray(data.patients) || !Array.isArray(data.records)) {
        throw new Error('形式が正しくありません');
      }
      if (!confirm(`患者 ${data.patients.length} 名、診療記録 ${data.records.length} 件を読み込みます。\n現在のデータは置き換えられます。よろしいですか?`)) return;
      state = {
        patients: data.patients,
        records: data.records,
        nextPatientNo: data.nextPatientNo || data.patients.length + 1,
      };
      selectedPatientId = null;
      saveState();
      renderPatientList();
      renderMain();
      alert('読み込みが完了しました。');
    } catch (e) {
      alert('ファイルの読み込みに失敗しました:' + e.message);
    }
  };
  reader.readAsText(file);
}

/* ---------------- イベント設定 ---------------- */

function setupEvents() {
  $('#patient-search').addEventListener('input', renderPatientList);
  $('#btn-new-patient').addEventListener('click', () => openPatientDialog());

  $('#patient-list').addEventListener('click', (e) => {
    const li = e.target.closest('li[data-id]');
    if (!li) return;
    selectedPatientId = li.dataset.id;
    recordFilter = { keyword: '', department: '' };
    renderPatientList();
    renderMain();
  });

  // メイン領域のボタンはイベント委譲でまとめて処理する
  $('#main').addEventListener('click', (e) => {
    const btn = e.target.closest('[data-action]');
    if (!btn) return;
    const patient = state.patients.find((p) => p.id === selectedPatientId);
    switch (btn.dataset.action) {
      case 'edit-patient': openPatientDialog(patient); break;
      case 'delete-patient': deletePatient(patient.id); break;
      case 'new-record': openRecordDialog(); break;
      case 'edit-record': openRecordDialog(state.records.find((r) => r.id === btn.dataset.id)); break;
      case 'delete-record': deleteRecord(btn.dataset.id); break;
      case 'print': window.print(); break;
    }
  });

  $('#main').addEventListener('input', (e) => {
    // フリーメモは入力のたびに自動保存する(画面全体は再描画しない)
    if (e.target.id === 'patient-memo') {
      const patient = state.patients.find((p) => p.id === selectedPatientId);
      patient.memo = e.target.value;
      patient.memoUpdatedAt = new Date().toISOString();
      saveState();
      $('#memo-status').textContent = `保存しました ${formatDateTime(patient.memoUpdatedAt)}`;
      return;
    }
    if (e.target.id === 'record-keyword') {
      recordFilter.keyword = e.target.value;
      renderMain();
      // 再描画で入力欄が作り直されるので、フォーカスとカーソル位置を戻す
      const input = $('#record-keyword');
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
    }
  });
  $('#main').addEventListener('change', (e) => {
    if (e.target.id === 'record-department') {
      recordFilter.department = e.target.value;
      renderMain();
    }
  });

  $('#patient-form').addEventListener('submit', handlePatientSubmit);
  $('#record-form').addEventListener('submit', handleRecordSubmit);

  $('#btn-add-rx').addEventListener('click', () => addRxRow());
  $('#rx-rows').addEventListener('click', (e) => {
    if (e.target.matches('[data-remove-rx]')) {
      e.target.closest('tr').remove();
      checkRxAllergy();
    }
  });
  $('#rx-rows').addEventListener('input', checkRxAllergy);

  document.querySelectorAll('dialog [data-close]').forEach((btn) => {
    btn.addEventListener('click', () => btn.closest('dialog').close());
  });

  $('#btn-export').addEventListener('click', exportData);
  $('#input-import').addEventListener('change', (e) => {
    if (e.target.files[0]) importData(e.target.files[0]);
    e.target.value = '';
  });
}

loadState();
setupEvents();
renderPatientList();
renderMain();
