# Game Server For Cheat (also known as Balony or B.S.)

## Requirements for Running the Server

* The dotnet core runtime (currently built against v2.2)
* Access to a [redis](https://redis.io/) server to track the state of the application. It's suggested that you install/run redis via docker

## Requirements to build the server

* install [dotnet core](https://dotnet.microsoft.com/download) minimum version 2.2 
* *(optional)* install [FAKE](https://fake.build/) build tool as a global tool
`dotnet tool install fake-cli -g`
* *(optional)* install [Paket](https://fsprojects.github.io/Paket/) as a global tool
`dotnet tool install paket -g`
* The [Yarn](https://yarnpkg.com/lang/en/docs/install/) package manager (you an also use `npm` but the usage of `yarn` is encouraged).
* [Node LTS](https://nodejs.org/en/download/) installed for the front end components.
* If you're running on OSX or Linux, you'll also need to install [Mono](https://www.mono-project.com/docs/getting-started/install/).

_Fake_ and _Paket_ must be availble on your path to build the server. If you don't install them as global tools you'll need them in your environment.


## Running the server locally

To concurrently run the server and the client components in watch mode use the following command:

```bash
fake build -t Run
```

You can use the included `Dockerfile` and `build.fsx` script to deploy your application as Docker container. You can find more regarding this topic in the [official template documentation](https://safe-stack.github.io/docs/template-docker/).

When you run the application by default it's expecting an unauthenticated redis instance running on localhost on the default redis port. You can override this settings and provide your own connection string to a redis server by setting the _REDIS_SERVER_ environment variable to a valid redis connection string. The server will crash on startup if it is unable to connect to redis.

## Troubleshooting

* **fake not found** - If you fail to execute `fake` from command line after installing it as a global tool, you might need to add it to your `PATH` manually: (e.g. `export PATH="$HOME/.dotnet/tools:$PATH"` on unix) - [related GitHub issue](https://github.com/dotnet/cli/issues/9321)

## Building the Client Library

A partially client library is provided for python in the `clientlib/python` directory. This library targets python 3.x.x To build the client library:

1. Install [pipenv](https://github.com/pypa/pipenv)
2. Build an environment using the included pipfile
3. In a pipenv shell run `python setup.py -sdist` to build a source distribution

## Running the server in AWS with terraform 

Optionally you may want to run the server on a server to allow other players to play. 

The `terraform` directory includes a generic terraform install which can deploy the application to AWS (Amazon Web Services). It could also serve as a base to deploy other cloud platforms like heroku, google cloud, or azure.

1. Install [terraform](https://terraform.io)
2. Build the application into a docker container and upload it to an ECR repository in amazon (or other docker host)
3. Navgiate to the terraform directory in the console of your choice
4. Modify the "aws_ecr_repository" and "cheat_image" sections of ecs.tf to match your setup.
5. Run terraform up on the command line. This assumes the command line is setup properly with AWS keys and terraform is available on your path.


## Issues

This server should be considered in an alpha state. There are significant open [issues](https://github.com/amcguier/CheatCardGame/issues). It was setup to be run in an insecure environment and would need to be significantly hardened before being run in any public facing environment. Pull requests are welcome.


