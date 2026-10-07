"""Siglas e códigos IBGE usados na coleta e validação local."""
UFS = dict(zip(
    'RO AC AM RR PA AP TO MA PI CE RN PB PE AL SE BA MG ES RJ SP PR SC RS MS MT GO DF'.split(),
    '11 12 13 14 15 16 17 21 22 23 24 25 26 27 28 29 31 32 33 35 41 42 43 50 51 52 53'.split()))


def normalize_ufs(values):
    result = list(dict.fromkeys(value.strip().upper() for value in values))
    if not result or any(value not in UFS for value in result):
        raise ValueError('Informe uma ou mais UFs brasileiras válidas.')
    return result
