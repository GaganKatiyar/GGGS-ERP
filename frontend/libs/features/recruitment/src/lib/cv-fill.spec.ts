import type { CandidateDetailsInput } from '@gggs/shared/models';
import { cvFill, sameValue } from './recruitment-forms';

const blank: CandidateDetailsInput = { firstName: '', email: '', skills: [] };

describe('filling the candidate form from a CV', () => {
  it('fills only the fields that are still empty', () => {
    const fill = cvFill({ firstName: 'Riya', email: 'riya@example.com', currentCtc: 1_200_000, skills: ['Java'] }, { ...blank, email: 'typed@example.com' });

    expect(fill).toEqual({ firstName: 'Riya', currentCtc: 1_200_000, skills: ['Java'] });
  });

  it('adds nothing the CV did not give', () => {
    expect(cvFill({ skills: [] }, blank)).toEqual({});
  });

  it('compares skills as a list, so an edited list loses its "from CV" mark', () => {
    expect(sameValue(['Java', 'SQL'], ['Java', 'SQL'])).toBe(true);
    expect(sameValue(['Java', 'SQL'], ['Java'])).toBe(false);
    expect(sameValue('Riya', 'Riya')).toBe(true);
  });
});
