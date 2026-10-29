import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError

import cc_account
import worker


class ClaudeReconnectTests(unittest.TestCase):
    def test_new_credential_file_is_detected_once(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(cc_account, 'CRED_PATH', str(Path(directory) / 'credentials.json')), \
                patch.object(cc_account, '_credential_stamp', None):
            self.assertFalse(cc_account._credentials_changed())
            Path(cc_account.CRED_PATH).write_text('{}')
            self.assertTrue(cc_account._credentials_changed())
            self.assertFalse(cc_account._credentials_changed())

    def test_unchanged_credentials_keep_auth_backoff(self):
        with patch.object(cc_account, '_cache', {'state': 'expired'}), \
                patch.object(cc_account, '_next_fetch', 9999999999), \
                patch.object(cc_account, '_inflight', False), \
                patch.object(cc_account, '_credentials_changed', return_value=False), \
                patch.object(cc_account, '_refresh_locked') as fetch:
            cc_account.status(block=True)
            fetch.assert_not_called()

    def test_renewed_credentials_bypass_auth_backoff(self):
        with patch.object(cc_account, '_cache', {'state': 'expired'}), \
                patch.object(cc_account, '_next_fetch', 9999999999), \
                patch.object(cc_account, '_inflight', False), \
                patch.object(cc_account, '_credentials_changed', return_value=True), \
                patch.object(cc_account, '_refresh_locked') as fetch:
            cc_account.status(block=True)
            fetch.assert_called_once()

    def test_changed_credentials_do_not_bypass_rate_limit(self):
        with patch.object(cc_account, '_cache', {'state': 'rate-limited'}), \
                patch.object(cc_account, '_next_fetch', 9999999999), \
                patch.object(cc_account, '_inflight', False), \
                patch.object(cc_account, '_credentials_changed', return_value=True), \
                patch.object(cc_account, '_refresh_locked') as fetch:
            cc_account.status(block=True)
            fetch.assert_not_called()

    def test_expired_token_is_not_sent_or_rotated(self):
        with patch.object(cc_account, '_read_token', return_value={
                'token': 'PRIVATE', 'expires_at': 1}), \
                patch.object(cc_account._urlrequest, 'urlopen') as request:
            self.assertEqual('expired', cc_account._fetch()['state'])
            request.assert_not_called()

    def test_403_is_not_reported_as_expired_login(self):
        with patch.object(cc_account, '_read_token', return_value={
                'token': 'PRIVATE', 'expires_at': 9999999999}), \
                patch.object(cc_account._urlrequest, 'urlopen', side_effect=
                             HTTPError('https://example.test', 403, '', {}, None)):
            self.assertEqual('forbidden', cc_account._fetch()['state'])

    def test_bridge_auth_failure_does_not_hide_local_renewal(self):
        shared = {'claude': {'state': 'expired'},
                  'codex': {'state': 'ok', 'windows': []}}
        with patch.object(worker, 'bridge_accounts', return_value=shared), \
                patch.object(cc_account, 'status', return_value={'state': 'ok'}) as local, \
                patch.object(worker.codex_account, 'status') as codex:
            self.assertEqual('ok', worker.account_status(True)['claude']['state'])
            local.assert_called_once_with(True)
            codex.assert_not_called()


if __name__ == '__main__':
    unittest.main()
