import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

describe('smoke', () => {
  it('renders a heading', () => {
    render(<h1>Metro Ticket System</h1>);
    expect(screen.getByText('Metro Ticket System')).toBeInTheDocument();
  });
});