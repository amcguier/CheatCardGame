import requests
from urllib import parse

class Client():
    __api_url = ""
    __username = ""
    __game_running = False
    __game_id = ""
    __players = 0
    __players_connected = 0


    @property
    def game_id(self):
        return self.__game_id

    @property
    def players_connected(self):
        return self.__players_connected
    
    @property
    def players(self):
        return self.__players
    
    @property
    def api_url(self):
        return self.__api_url

    @api_url.setter
    def api_url(self,value):
        if not self.__game_running:
            self.__api_url = value
    
    def __init__(self,username,game_id=''):
        self.__api_url = "https://cheatcardgame.com"
        self.__username = username
        self.__game_id = game_id


    def __game_url(self):
        game_parts = "games/{}".format(self.game_id)
        return parse.urljoin(self.api_url,game_parts)

    def _update_game_state(self,game_dict):
        self.__game_id = game_dict['GameId']
        self.__players = game_dict['Players']
        self.__players_connected = game_dict['PlayersConnected']                                    
        
    def update_game(self):
        if self.game_id == "":
            return False

        url = self.__game_url()
        print("update url",url)
        response = requests.get(url)

        if response.status_code != 200:
            print(response.status_code)
            return False

        response_dict = response.json()

        self._update_game_state(response_dict)
        return True                    
        
    def create_game(self,players = 2):
        url = parse.urljoin(self.api_url,"games")
        print("start_game url: ",url)
        new_game = {}
        new_game['Username'] = self.__username
        new_game['Players'] = players
        response = requests.post(url,json=new_game)

        if response.status_code == 200:
            response_dict = response.json()
            self._update_game_state(response_dict)

            return True
        else:
            return False

    def list_games(self):
        url = parse.urljoin(self.api_url,"games")
        response = requests.get(url)

        if response.status_code == 200:
            return response.json()
        else:
            print("got an error message from the server")
            return None
        
    def join_game(self,player):
        pass
