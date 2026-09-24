// Rebindable keyboard/mouse input (port of GameInput.cs). Bindings use KeyboardEvent.code, or Mouse0..Mouse2.
export type Action = 'MoveUp' | 'MoveDown' | 'MoveLeft' | 'MoveRight' | 'Attack' | 'Aim' | 'Reload' | 'Interact' | 'Sprint' | 'Crouch'
  | 'Slot1' | 'Slot2' | 'Slot3' | 'Slot4' | 'Slot5' | 'Inventory' | 'Pause' | 'Medkit';

export const ACTIONS: [Action, string][] = [
  ['MoveUp', 'Move up'], ['MoveDown', 'Move down'], ['MoveLeft', 'Move left'], ['MoveRight', 'Move right'],
  ['Attack', 'Attack / shoot'], ['Aim', 'Aim / subdue'], ['Reload', 'Reload'], ['Interact', 'Interact'],
  ['Sprint', 'Sprint'], ['Crouch', 'Crouch'], ['Medkit', 'Use medkit'],
  ['Slot1', 'Weapon slot 1'], ['Slot2', 'Weapon slot 2'], ['Slot3', 'Weapon slot 3'], ['Slot4', 'Weapon slot 4'], ['Slot5', 'Weapon slot 5'],
  ['Inventory', 'Inventory'], ['Pause', 'Pause'],
];

// Crouch defaults to C in the browser: Ctrl+W would close the tab while crouch-walking.
export const DEFAULTS: Record<Action, string> = {
  MoveUp: 'KeyW', MoveDown: 'KeyS', MoveLeft: 'KeyA', MoveRight: 'KeyD', Attack: 'Mouse0', Aim: 'Mouse2', Reload: 'KeyR', Interact: 'KeyE',
  Sprint: 'ShiftLeft', Crouch: 'KeyC', Slot1: 'Digit1', Slot2: 'Digit2', Slot3: 'Digit3', Slot4: 'Digit4', Slot5: 'Digit5',
  Inventory: 'Tab', Pause: 'Escape', Medkit: 'KeyH',
};
const ALTERNATES: Partial<Record<Action, string>> = { MoveUp: 'ArrowUp', MoveDown: 'ArrowDown', MoveLeft: 'ArrowLeft', MoveRight: 'ArrowRight', Pause: 'KeyP' };

class Input {
  bindings: Record<Action, string> = { ...DEFAULTS };
  private down = new Set<string>();
  private pressed = new Set<string>();
  mouseX = 0; mouseY = 0;
  wheel = 0;
  capture: ((code: string) => void) | null = null;
  gameActive = false;

  attach(el: HTMLElement) {
    window.addEventListener('keydown', e => {
      if (this.capture) { e.preventDefault(); const c = this.capture; this.capture = null; c(e.code); return; }
      if (this.gameActive || e.code === 'Tab') e.preventDefault();
      if (!e.repeat) this.pressed.add(e.code);
      this.down.add(e.code);
    });
    window.addEventListener('keyup', e => this.down.delete(e.code));
    window.addEventListener('blur', () => this.down.clear());
    el.addEventListener('mousedown', e => {
      const code = 'Mouse' + e.button;
      if (this.capture) { e.preventDefault(); const c = this.capture; this.capture = null; c(code); return; }
      this.down.add(code); this.pressed.add(code);
    });
    window.addEventListener('mouseup', e => this.down.delete('Mouse' + e.button));
    window.addEventListener('mousemove', e => { this.mouseX = e.clientX; this.mouseY = e.clientY; });
    el.addEventListener('contextmenu', e => e.preventDefault());
    el.addEventListener('wheel', e => { this.wheel += Math.sign(e.deltaY); e.preventDefault(); }, { passive: false });
  }

  held(a: Action) { return this.down.has(this.bindings[a]) || (!!ALTERNATES[a] && this.down.has(ALTERNATES[a]!)); }
  wasPressed(a: Action) { return this.pressed.has(this.bindings[a]) || (!!ALTERNATES[a] && this.pressed.has(ALTERNATES[a]!)); }
  endFrame() { this.pressed.clear(); this.wheel = 0; }

  set(a: Action, code: string) {
    for (const k of Object.keys(this.bindings) as Action[]) if (k !== a && this.bindings[k] === code) this.bindings[k] = this.bindings[a];
    this.bindings[a] = code;
  }
  reset() { this.bindings = { ...DEFAULTS }; }
  load(saved: Record<string, string>) {
    this.reset();
    for (const [k, v] of Object.entries(saved ?? {})) if (k in DEFAULTS && typeof v === 'string') this.bindings[k as Action] = v;
  }
  label(a: Action) { return keyName(this.bindings[a]); }
}

export function keyName(code: string): string {
  const map: Record<string, string> = {
    Mouse0: 'LMB', Mouse1: 'MMB', Mouse2: 'RMB', ShiftLeft: 'Shift', ShiftRight: 'RShift', ControlLeft: 'Ctrl', ControlRight: 'RCtrl',
    AltLeft: 'Alt', Escape: 'Esc', Enter: 'Enter', Space: 'Space', Tab: 'Tab', Backquote: '`',
  };
  if (map[code]) return map[code];
  if (code.startsWith('Key')) return code.substring(3);
  if (code.startsWith('Digit')) return code.substring(5);
  if (code.startsWith('Arrow')) return code.substring(5);
  return code;
}

export const input = new Input();
