from setuptools import setup, find_packages

setup(
    name='cheat',
    version='0.0.2',
    description='a library for thee cheat card game',
    author='Andrew McGuier',
    author_email='andrew@echogatetech.com',
    classifiers = [
        'Programming Language :: Python :: 3',
        'Programming Language :: Python :: 3.4',
        'Programming Language :: Python :: 3.5',
        'Programming Language :: Python :: 3.6',
        'Programming Language :: Python :: 3.7',
        ],
    packages=find_packages(),
    python_requires='>=3.0, <4',
    install_requires=['requests'],
)

    
    
    
    
